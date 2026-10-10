using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using TrainingManagement.Api.Models;

namespace TrainingManagement.Api.Data;

public static class LegacyImport
{
    public static async Task ImportAsync(TrainingDbContext db, string path)
    {
        await using var transaction = await db.Database.BeginTransactionAsync();
        // Serialize the one-time import across hosts, independently from seats.
        await db.Database.ExecuteSqlRawAsync("""
            DECLARE @result int;
            EXEC @result = sys.sp_getapplock @Resource = 'TrainingManagement.LegacyImport',
                @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 10000;
            IF @result < 0 THROW 51001, 'Unable to acquire import lock.', 1;
            """);
        if (await db.Courses.AnyAsync() || await db.Employees.AnyAsync() || await db.Enrollments.AnyAsync()) return;
        if (!File.Exists(path)) throw new InvalidOperationException("指定的舊資料快照不存在；未建立示範資料，以避免覆蓋預期匯入內容。");

        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
        await using var stream = File.OpenRead(path);
        var snapshot = await JsonSerializer.DeserializeAsync<Snapshot>(stream, options)
            ?? throw new InvalidOperationException("舊資料快照沒有有效內容。");
        ValidateSnapshot(snapshot);

        // New identity values are mapped consistently instead of modifying SQL
        // Server identity settings. The source snapshot and SQLite are untouched.
        var courseMap = snapshot.Courses.ToDictionary(x => x.Id, x => new Course
        {
            Title = x.Title, Description = x.Description, Category = x.Category, Instructor = x.Instructor,
            Location = x.Location, Format = x.Format, StartDate = Utc(x.StartDate), EndDate = Utc(x.EndDate),
            DurationHours = x.DurationHours, Capacity = x.Capacity, Status = x.Status
        });
        var employeeMap = snapshot.Employees.ToDictionary(x => x.Id, x => new Employee
        {
            EmployeeNumber = x.EmployeeNumber, Name = x.Name, Email = x.Email,
            Department = x.Department, JobTitle = x.JobTitle, IsActive = x.IsActive
        });
        db.Courses.AddRange(courseMap.Values);
        db.Employees.AddRange(employeeMap.Values);
        db.Enrollments.AddRange(snapshot.Enrollments.Select(x => new Enrollment
        {
            Course = courseMap[x.CourseId], Employee = employeeMap[x.EmployeeId], Status = x.Status,
            Progress = x.Progress, Score = x.Score, RegisteredAt = Utc(x.RegisteredAt),
            CompletedAt = x.CompletedAt is { } completed ? Utc(completed) : null
        }));
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
    }

    private static void ValidateSnapshot(Snapshot snapshot)
    {
        if (snapshot.Courses is null || snapshot.Employees is null || snapshot.Enrollments is null)
            throw new InvalidOperationException("舊資料快照缺少 courses、employees 或 enrollments。");
        if (snapshot.Courses.Any(x => x.Id <= 0) || snapshot.Employees.Any(x => x.Id <= 0)
            || snapshot.Enrollments.Any(x => x.Id <= 0)
            || snapshot.Courses.Select(x => x.Id).Distinct().Count() != snapshot.Courses.Length
            || snapshot.Employees.Select(x => x.Id).Distinct().Count() != snapshot.Employees.Length
            || snapshot.Enrollments.Select(x => x.Id).Distinct().Count() != snapshot.Enrollments.Length)
            throw new InvalidOperationException("舊資料快照的識別碼無效或重複。");
        if (snapshot.Employees.Select(x => x.EmployeeNumber?.ToUpperInvariant()).Distinct().Count() != snapshot.Employees.Length
            || snapshot.Employees.Select(x => x.Email?.ToLowerInvariant()).Distinct().Count() != snapshot.Employees.Length)
            throw new InvalidOperationException("舊資料快照有重複的員工編號或電子郵件。");
        var courses = snapshot.Courses.ToDictionary(x => x.Id);
        var employees = snapshot.Employees.ToDictionary(x => x.Id);
        var pairs = new HashSet<(int, int)>();
        foreach (var row in snapshot.Courses)
        {
            Validate(new CourseRequest
            {
                Title = row.Title, Description = row.Description, Category = row.Category,
                Instructor = row.Instructor, Location = row.Location, Format = row.Format,
                StartDate = new DateTimeOffset(Utc(row.StartDate)), EndDate = new DateTimeOffset(Utc(row.EndDate)),
                DurationHours = row.DurationHours, Capacity = row.Capacity, Status = row.Status
            });
        }
        foreach (var row in snapshot.Employees)
            Validate(new EmployeeRequest
            {
                EmployeeNumber = row.EmployeeNumber, Name = row.Name, Email = row.Email,
                Department = row.Department, JobTitle = row.JobTitle, IsActive = row.IsActive
            });
        foreach (var row in snapshot.Enrollments)
        {
            if (!courses.TryGetValue(row.CourseId, out var course) || !employees.TryGetValue(row.EmployeeId, out var employee)
                || !pairs.Add((row.CourseId, row.EmployeeId)))
                throw new InvalidOperationException("舊報名紀錄有不存在的課程、員工或重複關聯。");
            if (row.CourseTitle != course.Title || row.EmployeeName != employee.Name
                || row.EmployeeNumber != employee.EmployeeNumber || row.Department != employee.Department
                || row.RegisteredAt == default || !Enum.IsDefined(row.Status)
                || (row.Status == EnrollmentStatus.Completed) != row.CompletedAt.HasValue
                || (row.CompletedAt.HasValue && Utc(row.CompletedAt.Value) < Utc(row.RegisteredAt)))
                throw new InvalidOperationException("舊報名紀錄的內容或完訓時間與來源資料不一致。");
            Validate(new UpdateEnrollmentRequest { Status = row.Status, Progress = row.Progress, Score = row.Score });
        }
        foreach (var course in snapshot.Courses)
        {
            var actual = snapshot.Enrollments.Where(x => x.CourseId == course.Id).ToList();
            if (actual.Count(x => x.Status != EnrollmentStatus.Cancelled) != course.EnrolledCount
                || actual.Count(x => x.Status == EnrollmentStatus.Completed) != course.CompletedCount
                || course.EnrolledCount > course.Capacity)
                throw new InvalidOperationException("舊課程的報名統計與報名紀錄不一致。");
        }
    }

    private static void Validate(object row)
    {
        var errors = new List<ValidationResult>();
        if (!Validator.TryValidateObject(row, new ValidationContext(row), errors, validateAllProperties: true))
            throw new InvalidOperationException("舊資料快照包含無效欄位；匯入已取消，請檢查來源內容。");
    }

    private static DateTime Utc(DateTime value) => value.Kind == DateTimeKind.Local
        ? value.ToUniversalTime() : DateTime.SpecifyKind(value, DateTimeKind.Utc);

    public sealed class Snapshot
    {
        [JsonRequired] public CourseResponse[] Courses { get; set; } = [];
        [JsonRequired] public EmployeeResponse[] Employees { get; set; } = [];
        [JsonRequired] public EnrollmentResponse[] Enrollments { get; set; } = [];
    }
}
