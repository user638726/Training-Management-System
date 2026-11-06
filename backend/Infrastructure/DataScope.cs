using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using TrainingManagement.Api.Data;
using TrainingManagement.Api.Models;

namespace TrainingManagement.Api.Infrastructure;

public sealed class DataScope(TrainingDbContext db, IHttpContextAccessor accessor)
{
    public ClaimsPrincipal? PrincipalOverride { get; set; }
    public ClaimsPrincipal User => PrincipalOverride ?? accessor.HttpContext?.User ?? new ClaimsPrincipal();
    public int UserId => int.TryParse(User.FindFirstValue("sub"), out var id) ? id : 0;
    public bool IsGlobal => User.IsInRole("Admin") || User.IsInRole("HR");
    public async Task<IQueryable<Employee>> Employees(CancellationToken ct = default)
    {
        if (IsGlobal) return db.Employees;
        if (User.IsInRole("Manager")) {
            var departments = await db.Departments.AsNoTracking().ToListAsync(ct);
            var ids = departments.Where(x => x.ManagerUserId == UserId).Select(x => x.Id).ToHashSet();
            while (true) {
                var children = departments.Where(x => x.ParentId.HasValue && ids.Contains(x.ParentId.Value)).Select(x => x.Id).ToList();
                if (!children.Any(ids.Add)) break;
            }
            var names = departments.Where(x => ids.Contains(x.Id)).Select(x => x.Name).ToArray();
            return db.Employees.Where(x => names.Contains(x.Department) || db.AppUsers.Any(u => u.Id == UserId && u.EmployeeId == x.Id));
        }
        if (User.IsInRole("Instructor"))
            return db.Employees.Where(x => x.Enrollments.Any(e => e.Course.InstructorUserId == UserId)
                || db.AppUsers.Any(u => u.Id == UserId && u.EmployeeId == x.Id));
        return db.Employees.Where(x => db.AppUsers.Any(u => u.Id == UserId && u.EmployeeId == x.Id));
    }
    public async Task<IQueryable<Enrollment>> Enrollments(CancellationToken ct = default)
    {
        var employees = await Employees(ct);
        var query = db.Enrollments.Where(x => employees.Any(e => e.Id == x.EmployeeId));
        if (User.IsInRole("Instructor")) query = query.Where(x => x.Course.InstructorUserId == UserId
            || db.AppUsers.Any(u => u.Id == UserId && u.EmployeeId == x.EmployeeId));
        return query;
    }
    public IQueryable<Course> Courses() => IsGlobal ? db.Courses : db.Courses.Where(x =>
        x.Status == CourseStatus.Published || (User.IsInRole("Instructor") && x.InstructorUserId == UserId)
        || x.Enrollments.Any(e => db.AppUsers.Any(u => u.Id == UserId && u.EmployeeId == e.EmployeeId)));
    public async Task RequireEmployee(int employeeId, CancellationToken ct = default)
    {
        if (!await (await Employees(ct)).AnyAsync(x => x.Id == employeeId, ct)) throw new ApiException(403, "您無權存取此員工資料。");
    }
    public async Task RequireTeacher(int courseId, CancellationToken ct = default)
    {
        if (!await db.Courses.AnyAsync(x => x.Id == courseId && (IsGlobal || (User.IsInRole("Instructor") && x.InstructorUserId == UserId)), ct))
            throw new ApiException(403, "僅人資或此課程的指定講師可以管理教材與出席。");
    }
    public static void CheckVersion(byte[] current, string? expected)
    {
        if (!string.IsNullOrEmpty(expected) && Convert.ToBase64String(current) != expected)
            throw new ApiException(409, "資料已由其他人更新，請重新整理後再編輯。");
    }
}
