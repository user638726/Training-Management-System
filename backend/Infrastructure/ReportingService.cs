using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using TrainingManagement.Api.Data;
using TrainingManagement.Api.Models;

namespace TrainingManagement.Api.Infrastructure;

public sealed class ReportingService(TrainingDbContext db, DataScope scope)
{
    public async Task<string> PermissionFingerprint(CancellationToken ct)
    {
        if (scope.IsGlobal) return "global";
        var ids = await (await scope.Employees(ct)).OrderBy(x => x.Id).Select(x => x.Id).ToListAsync(ct);
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(string.Join(",", ids))));
    }
    public async Task<DashboardResponse> Dashboard(CancellationToken ct)
    {
        var employees = await scope.Employees(ct); var courses = scope.Courses();
        var enrollments = (await scope.Enrollments(ct)).Where(x => x.Status != EnrollmentStatus.Cancelled);
        var total = await enrollments.CountAsync(ct); var completed = enrollments.Where(x => x.Status == EnrollmentStatus.Completed);
        var count = await completed.CountAsync(ct);
        var rows = await enrollments.GroupBy(x => x.DepartmentSnapshot ?? x.Employee.Department).Select(g => new {
            Department = g.Key, Enrollments = g.Count(), Completed = g.Count(x => x.Status == EnrollmentStatus.Completed),
            Hours = g.Sum(x => x.Status == EnrollmentStatus.Completed ? x.RecognizedHours ?? x.Course.DurationHours : 0)
        }).ToListAsync(ct);
        var employeeGroups = await employees.GroupBy(x => x.Department).Select(g => new { Department = g.Key, Count = g.Count(x => x.IsActive) }).ToListAsync(ct);
        var names = rows.Select(x => x.Department).Union(employeeGroups.Select(x => x.Department)).Order().ToList();
        var departments = names.Select(name => new DepartmentStatistics(name, employeeGroups.FirstOrDefault(x => x.Department == name)?.Count ?? 0,
            rows.FirstOrDefault(x => x.Department == name)?.Enrollments ?? 0, rows.FirstOrDefault(x => x.Department == name)?.Completed ?? 0,
            rows.FirstOrDefault(x => x.Department == name)?.Hours ?? 0)).ToList();
        var today = DateTime.UtcNow.AddHours(8); var month = new DateTime(today.Year, today.Month, 1).AddMonths(-5).AddHours(-8);
        var registeredGroups = await enrollments.Where(x => x.RegisteredAt >= month).GroupBy(x => new { x.RegisteredAt.AddHours(8).Year, x.RegisteredAt.AddHours(8).Month })
            .Select(g => new { g.Key.Year, g.Key.Month, Count = g.Count() }).ToListAsync(ct);
        var completedGroups = await completed.Where(x => x.CompletedAt >= month).GroupBy(x => new { x.CompletedAt!.Value.AddHours(8).Year, x.CompletedAt!.Value.AddHours(8).Month })
            .Select(g => new { g.Key.Year, g.Key.Month, Count = g.Count() }).ToListAsync(ct);
        var months = Enumerable.Range(0, 6).Select(i => month.AddHours(8).AddMonths(i)).Select(m => new MonthlyStatistics(
            m.ToString("yyyy-MM", CultureInfo.InvariantCulture), registeredGroups.FirstOrDefault(x => x.Year == m.Year && x.Month == m.Month)?.Count ?? 0,
            completedGroups.FirstOrDefault(x => x.Year == m.Year && x.Month == m.Month)?.Count ?? 0)).ToList();
        return new DashboardResponse(await employees.CountAsync(x => x.IsActive, ct), await courses.CountAsync(ct),
            await courses.CountAsync(x => x.Status == CourseStatus.Published, ct), total, count,
            total == 0 ? 0 : decimal.Round(count * 100m / total, 1), await completed.SumAsync(x => x.RecognizedHours ?? x.Course.DurationHours, ct), departments, months);
    }
    public async Task<IQueryable<Enrollment>> Rows(DateTime? from, DateTime? to, string? department, CancellationToken ct, bool includeCancelled = false)
    {
        if (from.HasValue && to.HasValue && from >= to) throw new ApiException(400, "報表結束時間須晚於開始時間。");
        var rows = await scope.Enrollments(ct);
        if (!includeCancelled) rows = rows.Where(x => x.Status != EnrollmentStatus.Cancelled);
        if (from.HasValue) rows = rows.Where(x => (x.CompletedAt ?? x.RegisteredAt) >= from.Value);
        if (to.HasValue) rows = rows.Where(x => (x.CompletedAt ?? x.RegisteredAt) < to.Value);
        if (!string.IsNullOrWhiteSpace(department)) rows = rows.Where(x => (x.DepartmentSnapshot ?? x.Employee.Department) == department);
        return rows;
    }
    public async Task<object> Management(DateTime? from, DateTime? to, string? department, CancellationToken ct)
    {
        var rows = await Rows(from, to, department, ct); var employees = await scope.Employees(ct);
        var assignments = db.Assignments.Where(x => employees.Any(e => e.Id == x.EmployeeId) && (department == null || x.Employee.Department == department)
            && (!scope.User.IsInRole("Instructor") || x.Course.InstructorUserId == scope.UserId || db.AppUsers.Any(u => u.Id == scope.UserId && u.EmployeeId == x.EmployeeId)));
        if (from.HasValue) assignments = assignments.Where(x => x.AssignedAt >= from);
        if (to.HasValue) assignments = assignments.Where(x => x.AssignedAt < to);
        var certificates = db.Certificates.Where(x => x.RevokedAt == null && rows.Any(e => e.Id == x.EnrollmentId));
        var departments = await assignments.GroupBy(x => x.Employee.Department).Select(g => new {
            department = g.Key, assigned = g.Count(), completed = g.Count(x => x.CompletedAt != null),
            overdue = g.Count(x => x.CompletedAt == null && x.DueAt < DateTime.UtcNow)
        }).ToListAsync(ct);
        var totals = await rows.GroupBy(x => 1).Select(g => new { enrolled = g.Count(), completed = g.Count(x => x.Status == EnrollmentStatus.Completed),
            hours = g.Sum(x => x.Status == EnrollmentStatus.Completed ? x.RecognizedHours ?? x.Course.DurationHours : 0),
            cost = g.Sum(x => x.Status == EnrollmentStatus.Completed ? x.RecognizedCost ?? 0 : 0) }).SingleOrDefaultAsync(ct);
        var overdue = await assignments.Where(x => x.CompletedAt == null && x.DueAt < DateTime.UtcNow).OrderBy(x => x.DueAt).Take(200)
            .Select(x => new { x.Id, employeeName = x.Employee.Name, department = x.Employee.Department, courseTitle = x.Course.Title, x.DueAt }).ToListAsync(ct);
        var expiring = await certificates.Where(x => x.ExpiresAt >= DateTime.UtcNow && x.ExpiresAt <= DateTime.UtcNow.AddDays(30))
            .OrderBy(x => x.ExpiresAt).Take(200).Select(x => new { x.Id, x.EmployeeName, x.CourseTitle, x.ExpiresAt }).ToListAsync(ct);
        var scopedEnrollmentIds = rows.Select(x => x.Id);
        var satisfaction = await db.Surveys.Where(x => scopedEnrollmentIds.Contains(x.EnrollmentId)).Select(x => (double?)x.Rating).AverageAsync(ct);
        return new { enrolled = totals?.enrolled ?? 0, completed = totals?.completed ?? 0, hours = totals?.hours ?? 0,
            cost = totals?.cost ?? 0, satisfaction, departments, overdue, expiring };
    }
    public async Task<byte[]> Csv(DateTime? from, DateTime? to, string? department, CancellationToken ct, string? status = null)
    {
        var query = await Rows(from, to, string.IsNullOrWhiteSpace(department) ? null : department, ct, true);
        if (!string.IsNullOrWhiteSpace(status)) {
            if (!Enum.TryParse<EnrollmentStatus>(status, out var parsedStatus)) throw new ApiException(400, "學習狀態無效。");
            query = query.Where(x => x.Status == parsedStatus);
        }
        var rows = query.OrderBy(x => x.Id).Select(x => new { x.Employee.EmployeeNumber, x.Employee.Name,
            Department = x.DepartmentSnapshot ?? x.Employee.Department, Title = x.CourseTitleSnapshot ?? x.Course.Title,
            x.Status, x.Progress, x.Score, Hours = x.Status == EnrollmentStatus.Completed ? x.RecognizedHours ?? x.Course.DurationHours : 0,
            Cost = x.Status == EnrollmentStatus.Completed ? x.RecognizedCost ?? 0 : 0, x.RegisteredAt, x.CompletedAt, x.Attempt });
        if (await rows.CountAsync(ct) > 100000) throw new ApiException(400, "匯出超過十萬筆，請縮小日期或部門範圍。");
        var b = new StringBuilder("員工編號,姓名,部門,課程,狀態,進度,分數,認列時數,認列成本,報名時間,完訓時間,梯次\r\n");
        await foreach (var row in rows.AsAsyncEnumerable().WithCancellation(ct)) {
            object?[] values = [row.EmployeeNumber, row.Name, row.Department, row.Title, row.Status, row.Progress, row.Score,
                row.Hours, row.Cost, row.RegisteredAt.ToString("O"), row.CompletedAt?.ToString("O"), row.Attempt];
            b.AppendLine(string.Join(",", values.Select(CsvCell)));
        }
        return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(b.ToString())];
    }
    private static string CsvCell(object? value)
    {
        var s = Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
        if (s.TrimStart().StartsWith('=') || s.TrimStart().StartsWith('+') || s.TrimStart().StartsWith('-') || s.TrimStart().StartsWith('@')) s = "'" + s;
        return "\"" + s.Replace("\"", "\"\"") + "\"";
    }
}
