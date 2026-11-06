using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TrainingManagement.Api.Data;
using TrainingManagement.Api.Infrastructure;
using TrainingManagement.Api.Models;

namespace TrainingManagement.Api.Controllers;

[ApiController, Route("api/enterprise")]
public sealed class LearningController(TrainingDbContext db, DataScope scope, MutationGate gate, TrainingService training,
    EnterpriseJobs jobs, WorkflowService workflows) : ControllerBase
{
    [HttpGet("plans"), Authorize(Roles = Roles.Administrators)]
    public async Task<IActionResult> Plans(CancellationToken ct) => Ok(await db.LearningPlans.AsNoTracking().OrderByDescending(x => x.Id)
        .Select(x => new { x.Id, x.Title, x.Department, x.JobTitle, x.NewHireDays, x.DueDays, x.RecurrenceMonths, x.IsActive,
            courseIds = x.Courses.Select(c => c.CourseId).ToArray(), rowVersion = Convert.ToBase64String(x.RowVersion) }).ToListAsync(ct));
    [HttpPost("plans"), Authorize(Roles = Roles.Administrators)]
    public async Task<IActionResult> CreatePlan(PlanRequest r, CancellationToken ct)
    {
        using var write = await gate.EnterAsync(ct);
        await ValidatePlan(r, ct);
        var p = new LearningPlan { Title = r.Title.Trim(), Department = NullIfEmpty(r.Department), JobTitle = NullIfEmpty(r.JobTitle),
            NewHireDays = r.NewHireDays, DueDays = r.DueDays, RecurrenceMonths = r.RecurrenceMonths, IsActive = r.IsActive,
            Courses = r.CourseIds.Distinct().Select(id => new LearningPlanCourse { CourseId = id }).ToList() };
        db.LearningPlans.Add(p); await db.SaveChangesAsync(ct); return Created($"/api/enterprise/plans/{p.Id}", new { p.Id });
    }
    [HttpPut("plans/{id:int}"), Authorize(Roles = Roles.Administrators)]
    public async Task<IActionResult> UpdatePlan(int id, PlanRequest r, CancellationToken ct)
    {
        using var write = await gate.EnterAsync(ct);
        await ValidatePlan(r, ct);
        var p = await db.LearningPlans.Include(x => x.Courses).SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new ApiException(404, "找不到學習計畫。");
        DataScope.CheckVersion(p.RowVersion, r.RowVersion);
        p.Title = r.Title.Trim(); p.Department = NullIfEmpty(r.Department); p.JobTitle = NullIfEmpty(r.JobTitle);
        p.NewHireDays = r.NewHireDays; p.DueDays = r.DueDays; p.RecurrenceMonths = r.RecurrenceMonths; p.IsActive = r.IsActive;
        var ids = r.CourseIds.Distinct().ToHashSet();
        foreach (var c in p.Courses.Where(x => !ids.Contains(x.CourseId)).ToList()) p.Courses.Remove(c);
        foreach (var cid in ids.Where(cid => !p.Courses.Any(x => x.CourseId == cid))) p.Courses.Add(new LearningPlanCourse { CourseId = cid });
        await db.SaveChangesAsync(ct); return NoContent();
    }
    private async Task ValidatePlan(PlanRequest r, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(r.Title)) throw new ApiException(400, "請輸入計畫名稱。");
        if (await db.Courses.CountAsync(x => r.CourseIds.Contains(x.Id), ct) != r.CourseIds.Distinct().Count()) throw new ApiException(400, "課程不存在。");
        if (!string.IsNullOrWhiteSpace(r.Department) && !await db.Departments.AnyAsync(x => x.Name == r.Department.Trim(), ct)) throw new ApiException(400, "部門不存在。");
    }
    [HttpPost("plans/{id:int}/assign"), Authorize(Roles = Roles.Administrators)]
    public async Task<IActionResult> Assign(int id, CancellationToken ct)
    {
        if (!await db.LearningPlans.AnyAsync(x => x.Id == id && x.IsActive, ct)) throw new ApiException(404, "找不到啟用的學習計畫。");
        return Ok(new { assigned = await jobs.RunAssignments(id, ct) });
    }
    [HttpGet("assignments")]
    public async Task<IActionResult> Assignments(CancellationToken ct)
    {
        var employees = await scope.Employees(ct);
        return Ok(await db.Assignments.AsNoTracking().Where(x => employees.Any(e => e.Id == x.EmployeeId) && (!User.IsInRole("Instructor") || x.Course.InstructorUserId == scope.UserId || db.AppUsers.Any(u => u.Id == scope.UserId && u.EmployeeId == x.EmployeeId)))
            .OrderBy(x => x.CompletedAt != null).ThenBy(x => x.DueAt).Take(1000)
            .Select(x => new { x.Id, x.EmployeeId, employeeName = x.Employee.Name, department = x.Employee.Department,
                x.CourseId, courseTitle = x.Course.Title, planTitle = x.LearningPlan.Title, x.Cycle, x.AssignedAt, x.DueAt, x.CompletedAt }).ToListAsync(ct));
    }
    [HttpPost("approvals"), Authorize(Roles = "Employee,Manager,Instructor")]
    public async Task<IActionResult> RequestApproval(SelfEnrollmentRequest r, CancellationToken ct)
    {
        using var write = await gate.EnterAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await CourseWriteLock.AcquireAsync(db, r.CourseId, ct);
        var eid = await db.AppUsers.Where(x => x.Id == scope.UserId).Select(x => x.EmployeeId).SingleAsync(ct)
            ?? throw new ApiException(409, "帳號尚未綁定員工。");
        var course = await db.Courses.FindAsync([r.CourseId], ct) ?? throw new ApiException(404, "找不到課程。");
        if (course.Status != CourseStatus.Published || !course.RequiresApproval) throw new ApiException(409, "此課程未開放簽核報名，請使用直接報名。");
        if (await db.Enrollments.AnyAsync(x => x.EmployeeId == eid && x.CourseId == r.CourseId && x.Status != EnrollmentStatus.Cancelled, ct)
            || await db.Approvals.AnyAsync(x => x.EmployeeId == eid && x.CourseId == r.CourseId && x.Status == "Pending", ct))
            throw new ApiException(409, "已有有效報名或待簽核申請。");
        var approval = new EnrollmentApproval { CourseId = r.CourseId, EmployeeId = eid, RequestedCost = course.Cost };
        db.Approvals.Add(approval); await db.SaveChangesAsync(ct);
        var budgetOwner=course.BudgetId.HasValue?await db.Budgets.Where(x=>x.Id==course.BudgetId).Select(x=>(int?)x.OwnerUserId).SingleAsync(ct):null;
        var workflow=await workflows.Initialize("Enrollment",approval.Id,eid,course.ApprovalPolicyId,course.Cost,course.Title,budgetOwner,ct);approval.WorkflowId=workflow?.Id;
        var employee = await db.Employees.FindAsync([eid], ct);
        var departments = await db.Departments.AsNoTracking().ToListAsync(ct);
        var current = departments.FirstOrDefault(x => x.Name == employee!.Department); var reviewers = new HashSet<int>();
        while (current is not null) { if (current.ManagerUserId.HasValue) reviewers.Add(current.ManagerUserId.Value); current = departments.FirstOrDefault(x => x.Id == current.ParentId); }
        foreach (var u in await db.AppUsers.Where(x => x.IsActive && x.IsApproved && (x.Role == "Admin" || x.Role == "HR" || reviewers.Contains(x.Id))).ToListAsync(ct))
            await training.Notify(u.Id, "新的報名申請", $"{employee!.Name} 申請「{course.Title}」。", $"approval:{approval.Id}", "/approvals", ct);
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return StatusCode(201, new { approval.Id, approval.Status });
    }
    [HttpGet("approvals")]
    public async Task<IActionResult> Approvals(CancellationToken ct)
    {
        var employees = await scope.Employees(ct);
        return Ok(await db.Approvals.AsNoTracking().Where(x => employees.Any(e => e.Id == x.EmployeeId) && (!User.IsInRole("Instructor") || db.AppUsers.Any(u => u.Id == scope.UserId && u.EmployeeId == x.EmployeeId)))
            .OrderByDescending(x => x.RequestedAt).Take(500).Select(x => new {
                x.Id, x.CourseId, courseTitle = x.Course.Title, x.EmployeeId, employeeName = x.Employee.Name,
                department = x.Employee.Department, x.Status, x.RequestedAt, x.ReviewedAt, x.Comment,
                rowVersion = Convert.ToBase64String(x.RowVersion) }).ToListAsync(ct));
    }
    [HttpPost("approvals/{id:int}/decision"), Authorize(Roles = Roles.Reviewers)]
    public async Task<IActionResult> Decide(int id, ApprovalDecision r, CancellationToken ct)
    {
        using var write = await gate.EnterAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var a = await db.Approvals.Include(x => x.Course).SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new ApiException(404, "找不到申請。");
        await CourseWriteLock.AcquireAsync(db, a.CourseId, ct);
        DataScope.CheckVersion(a.RowVersion,r.RowVersion);
        if(a.WorkflowId.HasValue){
            var f=await db.Workflows.FindAsync([a.WorkflowId.Value],ct)??throw new ApiException(409,"找不到流程。");
            r.RowVersion=Convert.ToBase64String(f.RowVersion);await workflows.Decide(f.Id,r,ct);await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);return NoContent();
        }
        await scope.RequireEmployee(a.EmployeeId, ct);
        if (await db.AppUsers.AnyAsync(x => x.Id == scope.UserId && x.EmployeeId == a.EmployeeId, ct)) throw new ApiException(403, "不可簽核自己的申請。");
        if (a.Status != "Pending") throw new ApiException(409, "申請已處理。");
        if (r.Approve) { await training.Register(a.CourseId, a.EmployeeId, true, ct,a.WaitlistEntryId); if(a.WaitlistEntryId.HasValue){var w=await db.Waitlist.FindAsync([a.WaitlistEntryId.Value],ct);w!.Status="Accepted";} }
        a.Status = r.Approve ? "Approved" : "Rejected"; a.ReviewerId = scope.UserId; a.ReviewedAt = DateTime.UtcNow; a.Comment = r.Comment.Trim();
        await training.NotifyEmployee(a.EmployeeId, r.Approve ? "報名申請已核准" : "報名申請已退回", $"「{a.Course.Title}」：{a.Comment}", $"decision:{id}", ct);
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return NoContent();
    }
    [HttpPost("approvals/{id:int}/withdraw")]
    public async Task<IActionResult> Withdraw(int id, CancellationToken ct)
    {
        var a = await db.Approvals.SingleOrDefaultAsync(x => x.Id == id && db.AppUsers.Any(u => u.Id == scope.UserId && u.EmployeeId == x.EmployeeId), ct)
            ?? throw new ApiException(404, "找不到自己的申請。");
        if (a.Status != "Pending") throw new ApiException(409, "僅能撤回待簽核申請。");
        a.Status = "Withdrawn";
        if(a.WorkflowId.HasValue){var f=await db.Workflows.Include(x=>x.Stages).SingleAsync(x=>x.Id==a.WorkflowId,ct);f.Status="Withdrawn";f.UpdatedAt=DateTime.UtcNow;foreach(var s in f.Stages.Where(x=>x.Status=="Pending"||x.Status=="Waiting"))s.Status="Skipped";}
        if(a.WaitlistEntryId.HasValue){var w=await db.Waitlist.FindAsync([a.WaitlistEntryId.Value],ct);if(w is not null)w.Status="Cancelled";}
        await db.SaveChangesAsync(ct); return NoContent();
    }
    [HttpGet("courses/{id:int}/materials")]
    public async Task<IActionResult> Materials(int id, CancellationToken ct)
    {
        if (!await scope.Courses().AnyAsync(x => x.Id == id, ct)) throw new ApiException(404, "找不到課程。");
        return Ok(await db.Materials.Where(x => x.CourseId == id).Select(x => new { x.Id, x.FileName, x.ContentType, x.UploadedAt }).ToListAsync(ct));
    }
    [HttpPost("courses/{id:int}/materials"), Authorize(Roles = Roles.Teachers), RequestSizeLimit(11_000_000)]
    public async Task<IActionResult> Upload(int id, IFormFile file, CancellationToken ct)
    {
        await scope.RequireTeacher(id, ct);
        if (file.Length is <= 0 or > 10_000_000) throw new ApiException(400, "教材大小須介於 1 byte 與 10 MB。");
        var name = Path.GetFileName(file.FileName); var ext = Path.GetExtension(name).ToLowerInvariant();
        if (name.Length > 200 || ext is not (".pdf" or ".txt")) throw new ApiException(400, "僅接受 PDF 或 UTF-8 文字教材。");
        await using var stream = new MemoryStream(); await file.CopyToAsync(stream, ct); var bytes = stream.ToArray();
        if (ext == ".pdf" && (bytes.Length < 5 || Encoding.ASCII.GetString(bytes, 0, 5) != "%PDF-")) throw new ApiException(400, "PDF 檔案格式不正確。");
        if (ext == ".txt") { try { _ = new UTF8Encoding(false, true).GetString(bytes); } catch (DecoderFallbackException) { throw new ApiException(400, "文字教材須使用 UTF-8 編碼。"); } }
        var material = new CourseMaterial { CourseId = id, FileName = name, ContentType = ext == ".pdf" ? "application/pdf" : "text/plain", Content = bytes };
        db.Materials.Add(material); await db.SaveChangesAsync(ct); return StatusCode(201, new { material.Id });
    }
    [HttpGet("materials/{id:int}/download")]
    public async Task<IActionResult> DownloadMaterial(int id, CancellationToken ct)
    {
        var m = await db.Materials.SingleOrDefaultAsync(x => x.Id == id && scope.Courses().Any(c => c.Id == x.CourseId), ct)
            ?? throw new ApiException(404, "找不到教材。");
        Response.Headers.XContentTypeOptions = "nosniff";
        return File(m.Content, m.ContentType, m.FileName);
    }
    [HttpGet("courses/{id:int}/quiz")]
    public async Task<IActionResult> Quiz(int id, CancellationToken ct)
    {
        var c = await scope.Courses().SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new ApiException(404, "找不到課程。");
        var teacher = scope.IsGlobal || (User.IsInRole("Instructor") && c.InstructorUserId == scope.UserId);
        var questions = await db.QuizQuestions.Where(x => x.CourseId == id && x.Revision == c.QuizRevision).OrderBy(x => x.Id).ToListAsync(ct);
        return Ok(new { revision = c.QuizRevision, c.PassScore, questions = questions.Select(x => new { x.Id, x.Prompt,
            options = JsonSerializer.Deserialize<string[]>(x.OptionsJson), correctOption = teacher ? (int?)x.CorrectOption : null }) });
    }
    [HttpPut("courses/{id:int}/quiz"), Authorize(Roles = Roles.Teachers)]
    public async Task<IActionResult> SaveQuiz(int id, QuizRequest r, CancellationToken ct)
    {
        using var write = await gate.EnterAsync(ct); await scope.RequireTeacher(id, ct);
        var c = await db.Courses.FindAsync([id], ct) ?? throw new ApiException(404, "找不到課程。");
        DataScope.CheckVersion(c.RowVersion, r.RowVersion);
        if (r.Questions.Any(x => string.IsNullOrWhiteSpace(x.Prompt) || x.Options.Length < 2 || x.CorrectOption >= x.Options.Length
            || x.Options.Any(o => string.IsNullOrWhiteSpace(o) || o.Length > 500))) throw new ApiException(400, "題目與選項必填，每個選項最多 500 字，正解須在選項範圍內。");
        c.Revision++; c.QuizRevision++;
        db.QuizQuestions.AddRange(r.Questions.Select(x => new QuizQuestion { CourseId = id, Revision = c.QuizRevision,
            Prompt = x.Prompt.Trim(), OptionsJson = JsonSerializer.Serialize(x.Options), CorrectOption = x.CorrectOption }));
        await db.SaveChangesAsync(ct); return NoContent();
    }
    [HttpPost("enrollments/{id:int}/quiz")]
    public async Task<IActionResult> SubmitQuiz(int id, QuizSubmission r, CancellationToken ct)
    {
        using var write = await gate.EnterAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var e = await OwnEnrollment(id, ct);
        await CourseWriteLock.AcquireAsync(db, e.CourseId, ct);
        if (e.Status is EnrollmentStatus.Cancelled or EnrollmentStatus.Completed) throw new ApiException(409, "此報名狀態不可提交測驗。");
        if (r.Revision != e.Course.QuizRevision) throw new ApiException(409, "測驗已更新，請重新載入。");
        var questions = await db.QuizQuestions.Where(x => x.CourseId == e.CourseId && x.Revision == r.Revision).ToListAsync(ct);
        if (questions.Count == 0 || r.Answers.Count != questions.Count || questions.Any(x => !r.Answers.TryGetValue(x.Id, out var a)
            || a < 0 || a >= JsonSerializer.Deserialize<string[]>(x.OptionsJson)!.Length)) throw new ApiException(400, "請完整回答目前版本的所有題目。");
        var score = decimal.Round(questions.Count(x => r.Answers[x.Id] == x.CorrectOption) * 100m / questions.Count, 2);
        db.QuizAttempts.Add(new QuizAttempt { EnrollmentId = id, EnrollmentAttempt = e.Attempt, CourseRevision = r.Revision,
            Score = score, AnswersJson = JsonSerializer.Serialize(new { answers = r.Answers, questions = questions.Select(x => new { x.Id, x.Prompt, x.OptionsJson, x.CorrectOption }) }) });
        e.Score = Math.Max(e.Score ?? 0, score); e.Status = EnrollmentStatus.InProgress; e.Progress = Math.Max(e.Progress, 50);
        if (score >= e.Course.PassScore && (!e.Course.RequiresAttendance || e.Attended)) await training.Complete(e, ct);
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return Ok(new { score, passed = score >= e.Course.PassScore, status = e.Status });
    }
    [HttpPost("enrollments/{id:int}/attendance"), Authorize(Roles = Roles.Teachers)]
    public async Task<IActionResult> Attendance(int id, AttendanceRequest r, CancellationToken ct)
    {
        using var write = await gate.EnterAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var e = await db.Enrollments.Include(x => x.Course).Include(x => x.Employee).SingleOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new ApiException(404, "找不到報名紀錄。");
        await scope.RequireTeacher(e.CourseId, ct); DataScope.CheckVersion(e.RowVersion, r.RowVersion);
        if (e.Status == EnrollmentStatus.Cancelled) throw new ApiException(409, "已取消報名無法確認出席。");
        if (!r.Attended && e.Status == EnrollmentStatus.Completed) throw new ApiException(409, "請先由人資重新開啟完訓紀錄，再更正出席。");
        e.Attended = r.Attended;
        if (r.Attended && e.Course.RequiresQuiz && await db.QuizAttempts.AnyAsync(x => x.EnrollmentId == id && x.EnrollmentAttempt == e.Attempt
            && x.CourseRevision == e.Course.QuizRevision && x.Score >= e.Course.PassScore, ct)) await training.Complete(e, ct);
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return NoContent();
    }
    [HttpPost("enrollments/{id:int}/survey")]
    public async Task<IActionResult> Survey(int id, SurveyRequest r, CancellationToken ct)
    {
        var e = await OwnEnrollment(id, ct);
        if (e.Status != EnrollmentStatus.Completed) throw new ApiException(409, "完訓後才可填寫問卷。");
        db.Surveys.Add(new CourseSurvey { EnrollmentId = id, Attempt = e.Attempt, Rating = r.Rating, Feedback = r.Feedback.Trim() });
        await db.SaveChangesAsync(ct); return StatusCode(201);
    }
    private async Task<Enrollment> OwnEnrollment(int id, CancellationToken ct) => await db.Enrollments.Include(x => x.Course).Include(x => x.Employee)
        .SingleOrDefaultAsync(x => x.Id == id && db.AppUsers.Any(u => u.Id == scope.UserId && u.EmployeeId == x.EmployeeId), ct)
        ?? throw new ApiException(404, "找不到自己的報名紀錄。");
    [HttpGet("enrollments/{id:int}/history")]
    public async Task<IActionResult> History(int id, CancellationToken ct)
    {
        if (!await (await scope.Enrollments(ct)).AnyAsync(x => x.Id == id, ct)) throw new ApiException(404, "找不到報名紀錄。");
        return Ok(await db.EnrollmentHistories.Where(x => x.EnrollmentId == id).OrderByDescending(x => x.Id).ToListAsync(ct));
    }
    [HttpGet("certificates")]
    public async Task<IActionResult> Certificates(CancellationToken ct)
    {
        var enrollments = await scope.Enrollments(ct);
        return Ok(await db.Certificates.AsNoTracking().Where(x => enrollments.Any(e => e.Id == x.EnrollmentId))
            .OrderByDescending(x => x.IssuedAt).Take(1000).Select(x => new { x.Id, x.EmployeeId, x.Serial, x.EmployeeName, x.CourseTitle, x.Hours, x.IssuedAt, x.ExpiresAt, x.RevokedAt }).ToListAsync(ct));
    }
    [HttpGet("certificates/{id:int}/download")]
    public async Task<IActionResult> DownloadCertificate(int id, CancellationToken ct)
    {
        var enrollments = await scope.Enrollments(ct);
        var c = await db.Certificates.SingleOrDefaultAsync(x => x.Id == id && enrollments.Any(e => e.Id == x.EnrollmentId), ct)
            ?? throw new ApiException(404, "找不到證書。");
        if (c.RevokedAt.HasValue) throw new ApiException(409, "此證書已撤銷。");
        static string E(string value) => System.Net.WebUtility.HtmlEncode(value);
        var html = $"<!doctype html><html lang='zh-Hant'><meta charset='utf-8'><title>完訓證書</title><style>body{{font-family:system-ui;text-align:center;padding:60px;color:#174e43}}article{{border:6px double #174e43;padding:60px}}h1{{font-size:40px}}p{{line-height:2}}small{{color:#555}}@media print{{body{{padding:0}}}}</style><article><small>育成 · 企業訓練管理</small><h1>完訓證書</h1><h2>{E(c.EmployeeName)}</h2><p>已完成「{E(c.CourseTitle)}」<br>認列訓練時數：{c.Hours} 小時<br>核發日期：{c.IssuedAt:yyyy/MM/dd}<br>有效期限：{(c.ExpiresAt.HasValue ? c.ExpiresAt.Value.ToString("yyyy/MM/dd") : "無期限")}</p><small>證書編號：{E(c.Serial)}</small></article></html>";
        return File(Encoding.UTF8.GetBytes(html), "text/html; charset=utf-8", $"certificate-{c.Serial}.html");
    }
    [HttpGet("notifications")]
    public async Task<IActionResult> Notifications([FromQuery] bool unreadOnly = false, CancellationToken ct = default)
        => Ok(await db.Notifications.AsNoTracking().Where(x => x.UserId == scope.UserId && (!unreadOnly || x.ReadAt == null))
            .OrderByDescending(x => x.Id).Take(200).ToListAsync(ct));
    [HttpPost("notifications/{id:long}/read")]
    public async Task<IActionResult> Read(long id, CancellationToken ct)
    {
        var n = await db.Notifications.SingleOrDefaultAsync(x => x.Id == id && x.UserId == scope.UserId, ct) ?? throw new ApiException(404, "找不到通知。");
        n.ReadAt ??= DateTime.UtcNow; await db.SaveChangesAsync(ct); return NoContent();
    }
    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
