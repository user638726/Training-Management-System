using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TrainingManagement.Api.Data;
using TrainingManagement.Api.Infrastructure;
using TrainingManagement.Api.Models;
namespace TrainingManagement.Api.Controllers;
[ApiController,Route("api/advanced")]
public sealed class SessionsController(TrainingDbContext db,DataScope scope,AdvancedAccess access,MutationGate gate,TrainingService training,WaitlistService waitlist,WorkflowService workflows) : ControllerBase
{
    [HttpGet("series")]
    public async Task<IActionResult> Series(CancellationToken ct)=>Ok(await db.CourseSeries.AsNoTracking().Where(s=>scope.IsGlobal||scope.Courses().Any(c=>c.SeriesId==s.Id)).OrderBy(x=>x.Title).ToListAsync(ct));
    [HttpPost("series"),Authorize(Roles=Roles.Administrators)]
    public async Task<IActionResult> CreateSeries(SeriesRequest r,CancellationToken ct){var s=new CourseSeries{Title=r.Title.Trim(),Description=r.Description.Trim()};db.CourseSeries.Add(s);await db.SaveChangesAsync(ct);return StatusCode(201,new{s.Id});}
    [HttpPost("series/{id:int}/offerings"),Authorize(Roles=Roles.Administrators)]
    public async Task<IActionResult> Offering(int id,OfferingRequest r,CancellationToken ct)
    {
        if(r.StartsAt==default||r.EndsAt<=r.StartsAt)throw new ApiException(400,"班次時間不正確。");
        using var write=await gate.EnterAsync(ct);
        await using var tx=await db.Database.BeginTransactionAsync(ct);
        if(!await db.CourseSeries.AnyAsync(x=>x.Id==id,ct))throw new ApiException(404,"課程系列不存在。");
        var source=await db.Courses.FindAsync([r.SourceCourseId],ct)??throw new ApiException(404,"來源課程不存在。");
        var c=new Course{SeriesId=id,Title=r.Title.Trim(),Description=source.Description,Category=source.Category,Instructor=source.Instructor,
            InstructorUserId=source.InstructorUserId,Format=source.Format,Location=r.Location.Trim(),StartDate=r.StartsAt.UtcDateTime,EndDate=r.EndsAt.UtcDateTime,
            DurationHours=source.DurationHours,Capacity=r.Capacity,Status=CourseStatus.Draft,RequiresApproval=source.RequiresApproval,RequiresAttendance=source.RequiresAttendance,
            RequiresQuiz=source.RequiresQuiz,PassScore=source.PassScore,Cost=source.Cost,PrerequisiteCourseId=source.PrerequisiteCourseId,
            CertificateValidityMonths=source.CertificateValidityMonths,ApprovalPolicyId=source.ApprovalPolicyId,BudgetId=source.BudgetId,CheckScheduleConflicts=true};
        db.Courses.Add(c);await db.SaveChangesAsync(ct);
        foreach(var q in await db.QuizQuestions.Where(x=>x.CourseId==source.Id&&x.Revision==source.QuizRevision).ToListAsync(ct))db.QuizQuestions.Add(new QuizQuestion{CourseId=c.Id,Revision=c.QuizRevision,Prompt=q.Prompt,OptionsJson=q.OptionsJson,CorrectOption=q.CorrectOption});
        await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);return StatusCode(201,new{c.Id,c.Title,status=c.Status.ToString()});
    }
    [HttpPut("courses/{id:int}/advanced"),Authorize(Roles=Roles.Administrators)]
    public async Task<IActionResult> Settings(int id,AdvancedCourseRequest r,CancellationToken ct)
    {
        using var write=await gate.EnterAsync(ct);await using var tx=await db.Database.BeginTransactionAsync(ct);await CourseWriteLock.AcquireAsync(db,id,ct);
        var c=await db.Courses.FindAsync([id],ct)??throw new ApiException(404,"找不到課程。");DataScope.CheckVersion(c.RowVersion,r.RowVersion);
        if(r.SeriesId.HasValue&&!await db.CourseSeries.AnyAsync(x=>x.Id==r.SeriesId,ct))throw new ApiException(400,"系列不存在。");
        if(r.PolicyId.HasValue&&!await db.ApprovalPolicies.AnyAsync(x=>x.Id==r.PolicyId&&x.IsActive,ct))throw new ApiException(400,"簽核流程不存在或已停用。");
        if(r.BudgetId.HasValue&&!await db.Budgets.AnyAsync(x=>x.Id==r.BudgetId&&x.IsActive,ct))throw new ApiException(400,"預算不存在或已停用。");
        c.SeriesId=r.SeriesId;c.ApprovalPolicyId=r.PolicyId;c.BudgetId=r.BudgetId;c.CheckScheduleConflicts=r.CheckScheduleConflicts;c.Revision++;
        if(r.PolicyId.HasValue)c.RequiresApproval=true;
        await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);return NoContent();
    }
    [HttpGet("courses/{id:int}/meetings")]
    public async Task<IActionResult> Meetings(int id,CancellationToken ct){if(!await scope.Courses().AnyAsync(x=>x.Id==id,ct))throw new ApiException(404,"找不到課程。");return Ok(await db.Meetings.Where(x=>x.CourseId==id).OrderBy(x=>x.StartsAt).ToListAsync(ct));}
    [HttpPost("courses/{id:int}/meetings"),Authorize(Roles=Roles.Administrators)]
    public async Task<IActionResult> AddMeeting(int id,MeetingRequest r,CancellationToken ct)
    {
        using var write=await gate.EnterAsync(ct);await using var tx=await db.Database.BeginTransactionAsync(ct);await CourseWriteLock.AcquireAsync(db,id,ct);
        var c=await db.Courses.FindAsync([id],ct)??throw new ApiException(404,"找不到課程。");
        if(r.StartsAt==default||r.EndsAt<=r.StartsAt||r.StartsAt.UtcDateTime<c.StartDate||r.EndsAt.UtcDateTime>c.EndDate)throw new ApiException(400,"場次須在課程起訖範圍內。");
        if(await db.Meetings.AnyAsync(x=>x.CourseId==id&&x.StartsAt<r.EndsAt.UtcDateTime&&x.EndsAt>r.StartsAt.UtcDateTime,ct))throw new ApiException(409,"場次時間重疊。");
        var m=new CourseMeeting{CourseId=id,StartsAt=r.StartsAt.UtcDateTime,EndsAt=r.EndsAt.UtcDateTime,Location=r.Location.Trim()};db.Meetings.Add(m);
        foreach(var employee in await db.Enrollments.Where(x=>x.CourseId==id&&x.Status!=EnrollmentStatus.Cancelled).Select(x=>x.EmployeeId).ToListAsync(ct))await training.NotifyEmployee(employee,"課程場次異動",$"「{c.Title}」新增場次，請重新查看行事曆。",$"meeting:{id}:{r.StartsAt:O}:{employee}",ct);
        await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);return StatusCode(201,new{m.Id});
    }
    [HttpGet("waitlist")]
    public async Task<IActionResult> Waitlists(CancellationToken ct){var people=await access.People(ct);return Ok(await db.Waitlist.Where(x=>people.Any(p=>p.Id==x.EmployeeId)).OrderByDescending(x=>x.Id).Take(500).Select(x=>new{x.Id,x.CourseId,courseTitle=x.Course.Title,x.EmployeeId,employeeName=x.Employee.Name,x.Status,x.QueuedAt,x.OfferExpiresAt,rowVersion=Convert.ToBase64String(x.RowVersion)}).ToListAsync(ct));}
    [HttpPost("waitlist")]
    public async Task<IActionResult> Join(SelfEnrollmentRequest r,CancellationToken ct)
    {
        var eid=await access.Self(ct);using var write=await gate.EnterAsync(ct);await using var tx=await db.Database.BeginTransactionAsync(ct);await CourseWriteLock.AcquireAsync(db,r.CourseId,ct);
        var c=await db.Courses.FindAsync([r.CourseId],ct)??throw new ApiException(404,"找不到課程。");if(c.Status!=CourseStatus.Published)throw new ApiException(409,"僅已發布課程可候補。");
        if(await db.Enrollments.AnyAsync(x=>x.CourseId==c.Id&&x.EmployeeId==eid&&x.Status!=EnrollmentStatus.Cancelled,ct))throw new ApiException(409,"您已有有效報名。");
        var seats=await db.Enrollments.CountAsync(x=>x.CourseId==c.Id&&x.Status!=EnrollmentStatus.Cancelled,ct)+await db.Waitlist.CountAsync(x=>x.CourseId==c.Id&&x.Status=="Offered"&&x.OfferExpiresAt>DateTime.UtcNow,ct);
        if(seats<c.Capacity)throw new ApiException(409,"課程尚有名額，請直接報名或提出簽核申請。");
        var e=new WaitlistEntry{CourseId=c.Id,EmployeeId=eid};db.Waitlist.Add(e);await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);return StatusCode(201,new{e.Id,e.Status});
    }
    [HttpPost("waitlist/{id:int}/accept")]
    public async Task<IActionResult> Accept(int id,CancellationToken ct)
    {
        var eid=await access.Self(ct);using var write=await gate.EnterAsync(ct);await using var tx=await db.Database.BeginTransactionAsync(ct);
        var entry=await db.Waitlist.Include(x=>x.Course).SingleOrDefaultAsync(x=>x.Id==id&&x.EmployeeId==eid,ct)??throw new ApiException(404,"找不到自己的候補。");await CourseWriteLock.AcquireAsync(db,entry.CourseId,ct);
        if(entry.Status!="Offered"||entry.OfferExpiresAt<=DateTime.UtcNow)throw new ApiException(409,"候補名額已到期或尚未輪到您。");
        if(entry.Course.RequiresApproval){
            if(await db.Approvals.AnyAsync(x=>x.CourseId==entry.CourseId&&x.EmployeeId==eid&&x.Status=="Pending",ct))throw new ApiException(409,"已有待簽核申請。");
            var a=new EnrollmentApproval{CourseId=entry.CourseId,EmployeeId=eid,WaitlistEntryId=entry.Id,RequestedCost=entry.Course.Cost};db.Approvals.Add(a);await db.SaveChangesAsync(ct);
            var owner=entry.Course.BudgetId.HasValue?await db.Budgets.Where(x=>x.Id==entry.Course.BudgetId).Select(x=>(int?)x.OwnerUserId).SingleAsync(ct):null;
            var flow=await workflows.Initialize("Enrollment",a.Id,eid,entry.Course.ApprovalPolicyId,entry.Course.Cost,entry.Course.Title,owner,ct);a.WorkflowId=flow?.Id;
            if(flow is null)foreach(var uid in await db.AppUsers.Where(x=>x.IsActive&&x.IsApproved&&(x.Role=="Admin"||x.Role=="HR")).Select(x=>x.Id).ToListAsync(ct))await training.Notify(uid,"候補報名待簽核",$"「{entry.Course.Title}」有候補簽核，須於保留期限內處理。",$"wait-approval:{a.Id}","/approvals",ct);
            await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);return StatusCode(202,new{approvalId=a.Id,a.Status});
        }
        var enrollment=await training.Register(entry.CourseId,eid,true,ct,entry.Id);entry.Status="Accepted";await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);return Ok(new{enrollmentId=enrollment.Id});
    }
    [HttpPost("waitlist/{id:int}/cancel")]
    public async Task<IActionResult> Cancel(int id,CancellationToken ct)
    {
        var eid=await access.Self(ct);using var write=await gate.EnterAsync(ct);await using var tx=await db.Database.BeginTransactionAsync(ct);
        var e=await db.Waitlist.SingleOrDefaultAsync(x=>x.Id==id&&x.EmployeeId==eid,ct)??throw new ApiException(404,"找不到候補。");await CourseWriteLock.AcquireAsync(db,e.CourseId,ct);
        if(e.Status is not("Waiting" or "Offered"))throw new ApiException(409,"此候補已處理。");
        if(await db.Approvals.AnyAsync(x=>x.WaitlistEntryId==id&&x.Status=="Pending",ct))throw new ApiException(409,"請先撤回對應的簽核申請。");
        e.Status="Cancelled";await db.SaveChangesAsync(ct);await waitlist.Promote(e.CourseId,ct);await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);return NoContent();
    }
    [HttpGet("calendar")]
    public async Task<IActionResult> Calendar(CancellationToken ct)=>Ok(await CalendarRows(ct));
    private async Task<List<CalendarItem>> CalendarRows(CancellationToken ct)
    {
        var enrollments=await(await scope.Enrollments(ct)).Where(x=>x.Status!=EnrollmentStatus.Cancelled&&x.Course.Status!=CourseStatus.Cancelled).Include(x=>x.Course).Include(x=>x.Employee).ToListAsync(ct);
        var courseIds=enrollments.Select(x=>x.CourseId).Distinct().ToArray();var meetings=await db.Meetings.Where(x=>courseIds.Contains(x.CourseId)).ToListAsync(ct);var result=new List<CalendarItem>();
        foreach(var e in enrollments){var list=meetings.Where(x=>x.CourseId==e.CourseId).ToList();if(list.Count==0)result.Add(new($"enrollment-{e.Id}",e.Employee.Name,e.Course.Title,e.Course.StartDate,e.Course.EndDate,e.Course.Location));else foreach(var m in list)result.Add(new($"enrollment-{e.Id}-meeting-{m.Id}",e.Employee.Name,e.Course.Title,m.StartsAt,m.EndsAt,m.Location));}
        return result.OrderBy(x=>x.StartsAt).ToList();
    }
    public sealed record CalendarItem(string Id,string Employee,string Title,DateTime StartsAt,DateTime EndsAt,string Location);
    [HttpGet("calendar/export")]
    public async Task<IActionResult> Export(CancellationToken ct)
    {
        static string E(string s)=>s.Replace("\\","\\\\").Replace("\r","").Replace("\n","\\n").Replace(",","\\,").Replace(";","\\;");
        var b=new StringBuilder("BEGIN:VCALENDAR\r\nVERSION:2.0\r\nPRODID:-//TrainingManagement//Calendar//ZH-TW\r\nCALSCALE:GREGORIAN\r\n");
        foreach(var e in await CalendarRows(ct))b.Append($"BEGIN:VEVENT\r\nUID:{e.Id}@training.local\r\nDTSTAMP:{DateTime.UtcNow:yyyyMMddTHHmmssZ}\r\nDTSTART:{e.StartsAt:yyyyMMddTHHmmssZ}\r\nDTEND:{e.EndsAt:yyyyMMddTHHmmssZ}\r\nSUMMARY:{E(e.Title)}\r\nDESCRIPTION:{E(e.Employee)}\r\nLOCATION:{E(e.Location)}\r\nEND:VEVENT\r\n");
        b.Append("END:VCALENDAR\r\n");
        static string Fold(string line){var output=new StringBuilder();var size=0;foreach(var rune in line.EnumerateRunes()){var length=rune.Utf8SequenceLength;if(size+length>75){output.Append("\r\n ");size=1;}output.Append(rune.ToString());size+=length;}return output.ToString();}
        var calendar=string.Join("\r\n",b.ToString().Split("\r\n").Select(Fold));return File(Encoding.UTF8.GetBytes(calendar),"text/calendar;charset=utf-8","training-calendar.ics");
    }
}
