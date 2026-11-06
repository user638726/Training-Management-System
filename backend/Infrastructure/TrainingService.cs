using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TrainingManagement.Api.Data;
using TrainingManagement.Api.Models;

namespace TrainingManagement.Api.Infrastructure;

public sealed class TrainingService(TrainingDbContext db, BudgetService budgets, ScheduleService schedule)
{
    public async Task Notify(int? userId, string title, string body, string key, string link = "/learning", CancellationToken ct = default)
    {
        if (userId is null || await db.Notifications.AnyAsync(x => x.UserId == userId && x.DedupeKey == key, ct)
            || db.Notifications.Local.Any(x => x.UserId == userId && x.DedupeKey == key)) return;
        db.Notifications.Add(new Notification { UserId = userId.Value, Title = title, Body = body, DedupeKey = key, Link = link });
    }
    public async Task NotifyEmployee(int employeeId, string title, string body, string key, CancellationToken ct = default)
        => await Notify(await db.AppUsers.Where(x => x.EmployeeId == employeeId && x.IsActive && x.IsApproved)
            .Select(x => (int?)x.Id).SingleOrDefaultAsync(ct), title, body, key, ct: ct);

    // Caller holds both a transaction and CourseWriteLock for this course.
    public async Task<Enrollment> Register(int courseId, int employeeId, bool approvalGranted, CancellationToken ct, int? waitlistId = null)
    {
        var course = await db.Courses.FindAsync([courseId], ct) ?? throw new ApiException(404, "找不到此課程。");
        var employee = await db.Employees.FindAsync([employeeId], ct) ?? throw new ApiException(404, "找不到此員工。");
        if (course.Status != CourseStatus.Published || !employee.IsActive) throw new ApiException(409, "僅已發布課程及在職員工可報名。");
        if (course.RequiresApproval && !approvalGranted) throw new ApiException(409, "此課程需先提出報名申請並由主管核准。");
        if (course.PrerequisiteCourseId is int prerequisite && !await db.Enrollments.AnyAsync(x =>
            x.EmployeeId == employeeId && x.CourseId == prerequisite && x.Status == EnrollmentStatus.Completed, ct))
            throw new ApiException(409, "請先完成先修課程。");
        if(course.CheckScheduleConflicts) await AdvancedAccess.Lock(db,$"employee-schedule:{employeeId}",ct);
        await schedule.EnsureAvailable(course,employeeId,ct);
        if(waitlistId.HasValue && !await db.Waitlist.AnyAsync(x=>x.Id==waitlistId && x.CourseId==courseId && x.EmployeeId==employeeId && x.Status=="Offered" && x.OfferExpiresAt>DateTime.UtcNow,ct)) throw new ApiException(409,"候補保留已失效。");
        var enrollment = await db.Enrollments.Include(x => x.Course).Include(x => x.Employee)
            .SingleOrDefaultAsync(x => x.CourseId == courseId && x.EmployeeId == employeeId, ct);
        if (enrollment is not null && enrollment.Status != EnrollmentStatus.Cancelled) throw new ApiException(409, "此員工已報名本課程。");
        if (await db.Enrollments.CountAsync(x => x.CourseId == courseId && x.Status != EnrollmentStatus.Cancelled, ct)
            + await db.Waitlist.CountAsync(x=>x.CourseId==courseId && x.Id!=waitlistId && x.Status=="Offered" && x.OfferExpiresAt>DateTime.UtcNow,ct) >= course.Capacity)
            throw new ApiException(409, "此課程報名人數已額滿。");
        if (enrollment is not null) { Archive(enrollment); enrollment.Attempt++; }
        else { enrollment = new Enrollment { Course = course, Employee = employee }; db.Enrollments.Add(enrollment); }
        enrollment.Status = EnrollmentStatus.Registered; enrollment.Progress = 0; enrollment.Score = null;
        enrollment.RegisteredAt = DateTime.UtcNow; enrollment.CompletedAt = null; enrollment.Attended = false;
        enrollment.RecognizedHours = null; enrollment.RecognizedCost = null;
        enrollment.CourseTitleSnapshot = null; enrollment.DepartmentSnapshot = null; enrollment.CourseRevisionSnapshot = null;
        await budgets.Reserve(enrollment,ct);
        await NotifyEmployee(employeeId, "報名已確認", $"您已報名「{course.Title}」。", $"registered:{courseId}:{employeeId}:{enrollment.Attempt}", ct);
        return enrollment;
    }
    public void Archive(Enrollment enrollment) => db.EnrollmentHistories.Add(new EnrollmentHistory {
        EnrollmentId = enrollment.Id, Attempt = enrollment.Attempt,
        SnapshotJson = JsonSerializer.Serialize(new { enrollment.Status, enrollment.Progress, enrollment.Score,
            enrollment.RegisteredAt, enrollment.CompletedAt, enrollment.Attended, enrollment.RecognizedHours,
            enrollment.RecognizedCost, enrollment.CourseTitleSnapshot, enrollment.DepartmentSnapshot, enrollment.CourseRevisionSnapshot })
    });
    public async Task Complete(Enrollment e, CancellationToken ct)
    {
        if (e.Course.RequiresAttendance && !e.Attended) throw new ApiException(409, "尚未完成出席確認。");
        if (e.Course.RequiresQuiz && !await db.QuizAttempts.AnyAsync(x => x.EnrollmentId == e.Id
            && x.EnrollmentAttempt == e.Attempt && x.CourseRevision == e.Course.QuizRevision && x.Score >= e.Course.PassScore, ct)
            && !db.QuizAttempts.Local.Any(x => x.EnrollmentId == e.Id && x.EnrollmentAttempt == e.Attempt
                && x.CourseRevision == e.Course.QuizRevision && x.Score >= e.Course.PassScore))
            throw new ApiException(409, "尚未通過此版本課程的測驗。");
        e.CompletedAt ??= DateTime.UtcNow; e.Status = EnrollmentStatus.Completed; e.Progress = 100;
        e.RecognizedHours ??= e.Course.DurationHours; e.RecognizedCost ??= await budgets.Consume(e,ct) ?? e.Course.Cost;
        e.CourseTitleSnapshot ??= e.Course.Title; e.DepartmentSnapshot ??= e.Employee.Department;
        e.CourseRevisionSnapshot ??= e.Course.Revision;
        var assignments = await db.Assignments.Where(x => x.EmployeeId == e.EmployeeId && x.CourseId == e.CourseId
            && x.CompletedAt == null && x.AssignedAt <= e.CompletedAt).ToListAsync(ct);
        foreach (var a in assignments) a.CompletedAt = e.CompletedAt;
        if (!await db.Certificates.AnyAsync(x => x.EnrollmentId == e.Id && x.Attempt == e.Attempt, ct))
            db.Certificates.Add(new Certificate { EnrollmentId = e.Id, Attempt = e.Attempt, EmployeeId = e.EmployeeId,
                EmployeeName = e.Employee.Name, CourseTitle = e.CourseTitleSnapshot, Hours = e.RecognizedHours.Value,
                ExpiresAt = e.Course.CertificateValidityMonths is int months ? e.CompletedAt.Value.AddMonths(months) : null });
        await NotifyEmployee(e.EmployeeId, "課程已完訓", $"「{e.CourseTitleSnapshot}」已完訓，可下載電子證書。", $"completed:{e.Id}:{e.Attempt}", ct);
    }
    public async Task Reopen(Enrollment e, CancellationToken ct)
    {
        if (e.Status != EnrollmentStatus.Completed) return;
        Archive(e);
        await budgets.Release(e,ct);
        foreach (var certificate in await db.Certificates.Where(x => x.EnrollmentId == e.Id && x.RevokedAt == null).ToListAsync(ct))
            certificate.RevokedAt = DateTime.UtcNow;
        e.Attempt++; e.CompletedAt = null; e.RecognizedHours = null; e.RecognizedCost = null;
        e.CourseTitleSnapshot = null; e.DepartmentSnapshot = null; e.CourseRevisionSnapshot = null;
        await budgets.Reserve(e,ct);
        foreach (var a in await db.Assignments.Where(x => x.EmployeeId == e.EmployeeId && x.CourseId == e.CourseId
            && x.CompletedAt != null && x.CompletedAt >= e.RegisteredAt).ToListAsync(ct)) a.CompletedAt = null;
    }
}
