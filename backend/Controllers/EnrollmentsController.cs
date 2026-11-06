using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using TrainingManagement.Api.Data;
using TrainingManagement.Api.Infrastructure;
using TrainingManagement.Api.Models;

namespace TrainingManagement.Api.Controllers;

[ApiController]
[Route("api/enrollments")]
public sealed class EnrollmentsController(TrainingDbContext db, MutationGate gate, DataScope scope, TrainingService training, BudgetService budgets, WaitlistService waitlist) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<EnrollmentResponse>>> GetAll([FromQuery] ListQuery filter, CancellationToken cancellationToken)
    {
        var query = (await scope.Enrollments(cancellationToken)).AsNoTracking();
        if (!string.IsNullOrWhiteSpace(filter.Search)) { var q = filter.Search.Trim(); query = query.Where(x => x.Employee.Name.Contains(q) || x.Employee.EmployeeNumber.Contains(q) || (x.CourseTitleSnapshot ?? x.Course.Title).Contains(q)); }
        if (!string.IsNullOrWhiteSpace(filter.Department)) query = query.Where(x => (x.DepartmentSnapshot ?? x.Employee.Department) == filter.Department);
        if (filter.CourseId.HasValue) query = query.Where(x => x.CourseId == filter.CourseId);
        if (!string.IsNullOrWhiteSpace(filter.Status)) { if (!Enum.TryParse<EnrollmentStatus>(filter.Status, out var status)) throw new ApiException(400, "學習狀態無效。"); query = query.Where(x => x.Status == status); }
        Response.Headers["X-Total-Count"] = (await query.CountAsync(cancellationToken)).ToString();
        var enrollments = await query.Include(x => x.Course).Include(x => x.Employee).OrderByDescending(x => x.RegisteredAt).ThenBy(x => x.Id)
            .Skip(filter.Offset).Take(filter.Limit).ToListAsync(cancellationToken);
        return Ok(enrollments.Select(ToResponse).ToList());
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<EnrollmentResponse>> Get(int id, CancellationToken cancellationToken)
    {
        var enrollment = await (await scope.Enrollments(cancellationToken)).AsNoTracking().Include(x => x.Course).Include(x => x.Employee)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (enrollment is null) throw new ApiException(404, "找不到此報名紀錄。");
        return Ok(ToResponse(enrollment));
    }

    [HttpGet("mine")]
    [Authorize(Roles = "Employee,Manager,Instructor")]
    public async Task<ActionResult<IReadOnlyList<EnrollmentResponse>>> Mine(CancellationToken cancellationToken)
    {
        var userId = int.Parse(User.FindFirstValue("sub")!);
        var employeeId = await db.AppUsers.Where(x => x.Id == userId).Select(x => x.EmployeeId)
            .SingleAsync(cancellationToken);
        if (employeeId is null) return Ok(Array.Empty<EnrollmentResponse>());
        var enrollments = await (await scope.Enrollments(cancellationToken)).AsNoTracking().Include(x => x.Course).Include(x => x.Employee)
            .Where(x => x.EmployeeId == employeeId).OrderByDescending(x => x.RegisteredAt)
            .ToListAsync(cancellationToken);
        return Ok(enrollments.Select(ToResponse).ToList());
    }

    [HttpPost("self")]
    [Authorize(Roles = "Employee,Manager,Instructor")]
    public async Task<ActionResult<EnrollmentResponse>> SelfEnroll(SelfEnrollmentRequest request, CancellationToken cancellationToken)
    {
        var userId = int.Parse(User.FindFirstValue("sub")!);
        return await RegisterForEmployee(request.CourseId, null, userId, cancellationToken);
    }

    [HttpPost("{id:int}/cancel")]
    [Authorize(Roles = "Employee,Manager,Instructor")]
    public async Task<ActionResult<EnrollmentResponse>> SelfCancel(int id, CancellationToken cancellationToken)
    {
        using var write = await gate.EnterAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var courseId = await db.Enrollments.Where(x => x.Id == id).Select(x => (int?)x.CourseId)
            .SingleOrDefaultAsync(cancellationToken);
        if (!courseId.HasValue) throw new ApiException(404, "找不到此報名紀錄。");
        await CourseWriteLock.AcquireAsync(db, courseId.Value, cancellationToken);
        var userId = int.Parse(User.FindFirstValue("sub")!);
        var employeeId = await LinkedEmployeeIdForUpdate(userId, cancellationToken);
        var enrollment = await db.Enrollments.Include(x => x.Course).Include(x => x.Employee)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (enrollment is null) throw new ApiException(404, "找不到此報名紀錄。");
        if (enrollment.EmployeeId != employeeId)
            throw new ApiException(403, "只能取消自己的報名紀錄。");
        if (enrollment.Status is EnrollmentStatus.Cancelled or EnrollmentStatus.Completed)
            throw new ApiException(409, "此報名狀態無法取消。");
        await budgets.Release(enrollment,cancellationToken);
        enrollment.Status = EnrollmentStatus.Cancelled;
        enrollment.CompletedAt = null;
        await db.SaveChangesAsync(cancellationToken);
        if(enrollment.Status==EnrollmentStatus.Cancelled){await waitlist.Promote(enrollment.CourseId,cancellationToken);await db.SaveChangesAsync(cancellationToken);}
        await transaction.CommitAsync(cancellationToken);
        return Ok(ToResponse(enrollment));
    }

    [HttpPost]
    [Authorize(Roles = Roles.Administrators)]
    public async Task<ActionResult<EnrollmentResponse>> Create(CreateEnrollmentRequest request, CancellationToken cancellationToken)
        => await RegisterForEmployee(request.CourseId, request.EmployeeId, null, cancellationToken);

    private async Task<ActionResult<EnrollmentResponse>> RegisterForEmployee(int courseId, int? employeeId, int? userId,
        CancellationToken cancellationToken)
    {
        using var write = await gate.EnterAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await CourseWriteLock.AcquireAsync(db, courseId, cancellationToken);
        if (userId is int authenticatedUserId)
        {
            employeeId = await LinkedEmployeeIdForUpdate(authenticatedUserId, cancellationToken);
        }
        var enrollment = await training.Register(courseId, employeeId!.Value, userId == null, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = enrollment.Id }, ToResponse(enrollment));
    }

    private async Task<int> LinkedEmployeeIdForUpdate(int userId, CancellationToken cancellationToken)
    {
        // Hold the account row until commit so an employee assignment cannot race a self-service change.
        var account = await db.AppUsers.FromSqlInterpolated($"""
            SELECT * FROM dbo.AppUsers WITH (UPDLOCK, HOLDLOCK) WHERE Id = {userId}
            """).SingleAsync(cancellationToken);
        return account.EmployeeId ?? throw new ApiException(409, "帳號尚無員工資料，請聯絡管理員建立。");
    }

    [HttpPut("{id:int}")]
    [Authorize(Roles = Roles.Administrators)]
    public async Task<ActionResult<EnrollmentResponse>> Update(int id, UpdateEnrollmentRequest request, CancellationToken cancellationToken)
    {
        using var write = await gate.EnterAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var courseId = await db.Enrollments.Where(x => x.Id == id).Select(x => (int?)x.CourseId)
            .SingleOrDefaultAsync(cancellationToken);
        if (!courseId.HasValue) throw new ApiException(404, "找不到此報名紀錄。");
        await CourseWriteLock.AcquireAsync(db, courseId.Value, cancellationToken);
        var enrollment = await db.Enrollments.Include(x => x.Course).Include(x => x.Employee)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (enrollment is null) throw new ApiException(404, "找不到此報名紀錄。");
        if (enrollment.Status == EnrollmentStatus.Cancelled && request.Status != EnrollmentStatus.Cancelled)
        {
            await training.Register(enrollment.CourseId,enrollment.EmployeeId,true,cancellationToken);
        }
        DataScope.CheckVersion(enrollment.RowVersion, request.RowVersion);
        if (request.Status != EnrollmentStatus.Completed) await training.Reopen(enrollment, cancellationToken);
        if (request.Status == EnrollmentStatus.Completed) await training.Complete(enrollment, cancellationToken);
        else enrollment.CompletedAt = null;
        if(request.Status==EnrollmentStatus.Cancelled) await budgets.Release(enrollment,cancellationToken);
        enrollment.Status = request.Status;
        enrollment.Progress = request.Progress;
        enrollment.Score = request.Score;
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Ok(ToResponse(enrollment));
    }

    [HttpDelete("{id:int}")]
    [Authorize(Roles = Roles.Administrators)]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        using var write = await gate.EnterAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var courseId = await db.Enrollments.Where(x => x.Id == id).Select(x => (int?)x.CourseId)
            .SingleOrDefaultAsync(cancellationToken);
        if (!courseId.HasValue) throw new ApiException(404, "找不到此報名紀錄。");
        await CourseWriteLock.AcquireAsync(db, courseId.Value, cancellationToken);
        var enrollment = await db.Enrollments.FindAsync([id], cancellationToken);
        if (enrollment is null) throw new ApiException(404, "找不到此報名紀錄。");
        throw new ApiException(409, "企業訓練紀錄不可刪除，請改為取消報名以保留歷程。");
    }

    private static void EnsureRegistrationAllowed(Course course, Employee employee)
    {
        if (course.Status != CourseStatus.Published)
            throw new ApiException(409, "只有已發布的課程可以接受報名。");
        if (!employee.IsActive) throw new ApiException(409, "停用的員工無法報名課程。");
    }

    private async Task EnsureCapacity(Course course, CancellationToken cancellationToken)
    {
        var count = await db.Enrollments.CountAsync(
            x => x.CourseId == course.Id && x.Status != EnrollmentStatus.Cancelled, cancellationToken);
        if (count >= course.Capacity) throw new ApiException(409, "此課程報名人數已額滿。");
    }

    private static EnrollmentResponse ToResponse(Enrollment enrollment) => new(enrollment.Id,
        enrollment.CourseId, enrollment.EmployeeId, enrollment.Employee.Name, enrollment.Employee.EmployeeNumber,
        enrollment.DepartmentSnapshot ?? enrollment.Employee.Department, enrollment.CourseTitleSnapshot ?? enrollment.Course.Title, enrollment.Status, enrollment.Progress,
        enrollment.Score, DateTime.SpecifyKind(enrollment.RegisteredAt, DateTimeKind.Utc),
        enrollment.CompletedAt is { } completed ? DateTime.SpecifyKind(completed, DateTimeKind.Utc) : null,
        enrollment.RecognizedHours, enrollment.Attended, enrollment.Attempt, Convert.ToBase64String(enrollment.RowVersion));
}
