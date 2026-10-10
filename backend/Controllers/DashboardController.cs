using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TrainingManagement.Api.Data;
using TrainingManagement.Api.Models;

namespace TrainingManagement.Api.Controllers;

[ApiController]
[Route("api/dashboard")]
public sealed class DashboardController(TrainingDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<DashboardResponse>> Get(CancellationToken cancellationToken)
    {
        // Keep the three queries on a consistent read while calculating attendee
        // hours. Cancelled records are excluded from enrollment statistics.
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken);
        var employees = await db.Employees.AsNoTracking().ToListAsync(cancellationToken);
        var courses = await db.Courses.AsNoTracking().ToListAsync(cancellationToken);
        var enrollments = await db.Enrollments.AsNoTracking().Include(x => x.Course).Include(x => x.Employee)
            .Where(x => x.Status != EnrollmentStatus.Cancelled).ToListAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var completed = enrollments.Where(x => x.Status == EnrollmentStatus.Completed).ToList();
        var departments = employees.Select(x => x.Department).Distinct().OrderBy(x => x).Select(department =>
        {
            var departmentEnrollments = enrollments.Where(x => x.Employee.Department == department).ToList();
            var departmentCompleted = departmentEnrollments.Where(x => x.Status == EnrollmentStatus.Completed).ToList();
            return new DepartmentStatistics(department,
                employees.Count(x => x.Department == department && x.IsActive), departmentEnrollments.Count,
                departmentCompleted.Count, departmentCompleted.Sum(x => x.Course.DurationHours));
        }).ToList();

        var todayTaipei = DateTime.UtcNow.AddHours(8);
        var currentMonth = new DateTime(todayTaipei.Year, todayTaipei.Month, 1);
        var months = Enumerable.Range(-5, 6).Select(offset => currentMonth.AddMonths(offset)).Select(month =>
        {
            var monthKey = month.ToString("yyyy-MM", CultureInfo.InvariantCulture);
            return new MonthlyStatistics(monthKey,
                enrollments.Count(x => TaipeiMonth(x.RegisteredAt) == monthKey),
                completed.Count(x => x.CompletedAt.HasValue && TaipeiMonth(x.CompletedAt.Value) == monthKey));
        }).ToList();

        return Ok(new DashboardResponse(employees.Count(x => x.IsActive), courses.Count,
            courses.Count(x => x.Status == CourseStatus.Published), enrollments.Count, completed.Count,
            enrollments.Count == 0 ? 0 : decimal.Round(completed.Count * 100m / enrollments.Count, 1),
            completed.Sum(x => x.Course.DurationHours), departments, months));
    }

    private static string TaipeiMonth(DateTime value) => value.AddHours(8).ToString("yyyy-MM", CultureInfo.InvariantCulture);
}
