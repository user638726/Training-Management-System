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
public sealed class EnrollmentsController(TrainingDbContext db, MutationGate gate) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<EnrollmentResponse>>> GetAll(CancellationToken cancellationToken)
    {
        var enrollments = await db.Enrollments.AsNoTracking().Include(x => x.Course).Include(x => x.Employee)
            .OrderByDescending(x => x.RegisteredAt).ToListAsync(cancellationToken);
        return Ok(enrollments.Select(ToResponse).ToList());
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<EnrollmentResponse>> Get(int id, CancellationToken cancellationToken)
    {
        var enrollment = await db.Enrollments.AsNoTracking().Include(x => x.Course).Include(x => x.Employee)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (enrollment is null) throw new ApiException(404, "找不到此報名紀錄。");
        return Ok(ToResponse(enrollment));
    }

    [HttpGet("mine")]
    [Authorize(Roles = "Employee")]
    public async Task<ActionResult<IReadOnlyList<EnrollmentResponse>>> Mine(CancellationToken cancellationToken)
    {
        var userId = int.Parse(User.FindFirstValue("sub")!);
        var employeeId = await db.AppUsers.Where(x => x.Id == userId).Select(x => x.EmployeeId)
            .SingleAsync(cancellationToken);
        if (employeeId is null) return Ok(Array.Empty<EnrollmentResponse>());
        var enrollments = await db.Enrollments.AsNoTracking().Include(x => x.Course).Include(x => x.Employee)
            .Where(x => x.EmployeeId == employeeId).OrderByDescending(x => x.RegisteredAt)
            .ToListAsync(cancellationToken);
        return Ok(enrollments.Select(ToResponse).ToList());
    }

    [HttpPost("self")]
    [Authorize(Roles = "Employee")]
    public async Task<ActionResult<EnrollmentResponse>> SelfEnroll(SelfEnrollmentRequest request, CancellationToken cancellationToken)
    {
        var userId = int.Parse(User.FindFirstValue("sub")!);
        return await RegisterForEmployee(request.CourseId, null, userId, cancellationToken);
    }

    [HttpPost("{id:int}/cancel")]
    [Authorize(Roles = "Employee")]
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
        enrollment.Status = EnrollmentStatus.Cancelled;
        enrollment.CompletedAt = null;
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Ok(ToResponse(enrollment));
    }

    [HttpPost]
    [Authorize(Roles = "Admin")]
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
        var course = await db.Courses.FindAsync([courseId], cancellationToken);
        if (course is null) throw new ApiException(404, "找不到此課程。");
        var employee = await db.Employees.FindAsync([employeeId!.Value], cancellationToken);
        if (employee is null) throw new ApiException(404, "找不到此員工。");
        EnsureRegistrationAllowed(course, employee);
        var existing = await db.Enrollments.SingleOrDefaultAsync(
            x => x.CourseId == courseId && x.EmployeeId == employeeId, cancellationToken);
        if (existing is not null && existing.Status != EnrollmentStatus.Cancelled)
            throw new ApiException(409, "此員工已報名本課程。");
        await EnsureCapacity(course, cancellationToken);

        var enrollment = existing ?? new Enrollment { Course = course, Employee = employee };
        enrollment.Status = EnrollmentStatus.Registered;
        enrollment.Progress = 0;
        enrollment.Score = null;
        enrollment.RegisteredAt = DateTime.UtcNow;
        enrollment.CompletedAt = null;
        if (existing is null) db.Enrollments.Add(enrollment);
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
    [Authorize(Roles = "Admin")]
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
            EnsureRegistrationAllowed(enrollment.Course, enrollment.Employee);
            await EnsureCapacity(enrollment.Course, cancellationToken);
        }
        enrollment.CompletedAt = request.Status == EnrollmentStatus.Completed
            ? enrollment.CompletedAt ?? DateTime.UtcNow : null;
        enrollment.Status = request.Status;
        enrollment.Progress = request.Progress;
        enrollment.Score = request.Score;
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Ok(ToResponse(enrollment));
    }

    [HttpDelete("{id:int}")]
    [Authorize(Roles = "Admin")]
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
        db.Enrollments.Remove(enrollment);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return NoContent();
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
        enrollment.Employee.Department, enrollment.Course.Title, enrollment.Status, enrollment.Progress,
        enrollment.Score, DateTime.SpecifyKind(enrollment.RegisteredAt, DateTimeKind.Utc),
        enrollment.CompletedAt is { } completed ? DateTime.SpecifyKind(completed, DateTimeKind.Utc) : null);
}
