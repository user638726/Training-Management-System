using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using TrainingManagement.Api.Data;
using TrainingManagement.Api.Infrastructure;
using TrainingManagement.Api.Models;

namespace TrainingManagement.Api.Controllers;

[ApiController]
[Route("api/courses")]
public sealed class CoursesController(TrainingDbContext db, MutationGate gate) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<CourseResponse>>> GetAll(CancellationToken cancellationToken)
    {
        var courses = await db.Courses.AsNoTracking().Include(x => x.Enrollments)
            .OrderByDescending(x => x.StartDate).ToListAsync(cancellationToken);
        return Ok(courses.Select(ToResponse).ToList());
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<CourseResponse>> Get(int id, CancellationToken cancellationToken)
    {
        var course = await db.Courses.AsNoTracking().Include(x => x.Enrollments)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (course is null) throw new ApiException(404, "找不到此課程。");
        return Ok(ToResponse(course));
    }

    [HttpPost]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<CourseResponse>> Create(CourseRequest request, CancellationToken cancellationToken)
    {
        using var write = await gate.EnterAsync(cancellationToken);
        var course = new Course();
        Apply(course, request);
        db.Courses.Add(course);
        await db.SaveChangesAsync(cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = course.Id }, ToResponse(course));
    }

    [HttpPut("{id:int}")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<CourseResponse>> Update(int id, CourseRequest request, CancellationToken cancellationToken)
    {
        using var write = await gate.EnterAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await CourseWriteLock.AcquireAsync(db, id, cancellationToken);
        var course = await db.Courses.Include(x => x.Enrollments).SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (course is null) throw new ApiException(404, "找不到此課程。");
        if (request.Capacity < course.Enrollments.Count(x => x.Status != EnrollmentStatus.Cancelled))
            throw new ApiException(409, "課程名額不能少於目前有效報名人數。");
        Apply(course, request);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Ok(ToResponse(course));
    }

    [HttpDelete("{id:int}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        using var write = await gate.EnterAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await CourseWriteLock.AcquireAsync(db, id, cancellationToken);
        var course = await db.Courses.FindAsync([id], cancellationToken);
        if (course is null) throw new ApiException(404, "找不到此課程。");
        if (await db.Enrollments.AnyAsync(x => x.CourseId == id, cancellationToken))
            throw new ApiException(409, "此課程已有報名紀錄，請保留課程或先移除報名紀錄。");
        db.Courses.Remove(course);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return NoContent();
    }

    private static void Apply(Course course, CourseRequest request)
    {
        course.Title = request.Title.Trim();
        course.Description = (request.Description ?? "").Trim();
        course.Category = request.Category.Trim();
        course.Instructor = request.Instructor.Trim();
        course.Location = request.Location.Trim();
        course.Format = request.Format;
        course.StartDate = request.StartDate.UtcDateTime;
        course.EndDate = request.EndDate.UtcDateTime;
        course.DurationHours = request.DurationHours;
        course.Capacity = request.Capacity;
        course.Status = request.Status;
    }

    private static CourseResponse ToResponse(Course course) => new(course.Id, course.Title, course.Description,
        course.Category, course.Instructor, course.Location, course.Format,
        DateTime.SpecifyKind(course.StartDate, DateTimeKind.Utc), DateTime.SpecifyKind(course.EndDate, DateTimeKind.Utc),
        course.DurationHours, course.Capacity, course.Status,
        course.Enrollments.Count(x => x.Status != EnrollmentStatus.Cancelled),
        course.Enrollments.Count(x => x.Status == EnrollmentStatus.Completed));
}
