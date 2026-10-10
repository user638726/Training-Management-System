using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace TrainingManagement.Api.Models;

public sealed class CourseRequest : IValidatableObject
{
    [Required(ErrorMessage = "請輸入課程名稱。"), StringLength(160)]
    public string Title { get; set; } = "";
    [StringLength(4000)]
    public string Description { get; set; } = "";
    [Required(ErrorMessage = "請輸入課程類別。"), StringLength(80)]
    public string Category { get; set; } = "";
    [Required(ErrorMessage = "請輸入講師。"), StringLength(100)]
    public string Instructor { get; set; } = "";
    [Required(ErrorMessage = "請輸入地點或線上會議連結。"), StringLength(300)]
    public string Location { get; set; } = "";
    [JsonRequired, EnumDataType(typeof(CourseFormat))]
    public CourseFormat Format { get; set; }
    [JsonRequired]
    public DateTimeOffset StartDate { get; set; }
    [JsonRequired]
    public DateTimeOffset EndDate { get; set; }
    [JsonRequired, Range(typeof(decimal), "0.25", "1000", ErrorMessage = "訓練時數必須介於 0.25 與 1000 之間。")]
    public decimal DurationHours { get; set; }
    [JsonRequired, Range(1, 10000, ErrorMessage = "名額必須介於 1 與 10000 之間。")]
    public int Capacity { get; set; }
    [JsonRequired, EnumDataType(typeof(CourseStatus))]
    public CourseStatus Status { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (StartDate == default) yield return new("請設定課程開始時間。", [nameof(StartDate)]);
        if (EndDate == default) yield return new("請設定課程結束時間。", [nameof(EndDate)]);
        if (EndDate <= StartDate) yield return new("結束時間必須晚於開始時間。", [nameof(EndDate)]);
    }
}

public sealed class EmployeeRequest
{
    [Required(ErrorMessage = "請輸入員工編號。"), StringLength(30),
     RegularExpression(@"^[A-Za-z0-9_-]+$", ErrorMessage = "員工編號只能包含英文、數字、底線與連字號。")]
    public string EmployeeNumber { get; set; } = "";
    [Required(ErrorMessage = "請輸入姓名。"), StringLength(100)]
    public string Name { get; set; } = "";
    [Required(ErrorMessage = "請輸入電子郵件。"), EmailAddress(ErrorMessage = "電子郵件格式不正確。"), StringLength(200)]
    public string Email { get; set; } = "";
    [Required(ErrorMessage = "請輸入部門。"), StringLength(80)]
    public string Department { get; set; } = "";
    [Required(ErrorMessage = "請輸入職稱。"), StringLength(100)]
    public string JobTitle { get; set; } = "";
    [JsonRequired]
    public bool IsActive { get; set; } = true;
}

public sealed class CreateEnrollmentRequest
{
    [JsonRequired, Range(1, int.MaxValue, ErrorMessage = "請選擇課程。")]
    public int CourseId { get; set; }
    [JsonRequired, Range(1, int.MaxValue, ErrorMessage = "請選擇員工。")]
    public int EmployeeId { get; set; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class SelfEnrollmentRequest
{
    [JsonRequired, Range(1, int.MaxValue, ErrorMessage = "請選擇課程。")]
    public int CourseId { get; set; }
}

public sealed class UpdateEnrollmentRequest : IValidatableObject
{
    [JsonRequired, EnumDataType(typeof(EnrollmentStatus))]
    public EnrollmentStatus Status { get; set; }
    [JsonRequired, Range(0, 100, ErrorMessage = "學習進度必須介於 0 與 100 之間。")]
    public int Progress { get; set; }
    [Range(typeof(decimal), "0", "100", ErrorMessage = "成績必須介於 0 與 100 之間。")]
    public decimal? Score { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Status == EnrollmentStatus.Completed && Progress != 100)
            yield return new("標記完訓時，學習進度必須為 100%。", [nameof(Progress)]);
        if (Status == EnrollmentStatus.Registered && Progress != 0)
            yield return new("已報名的學習進度必須為 0%。", [nameof(Progress)]);
    }
}

public sealed record CourseResponse(int Id, string Title, string Description, string Category,
    string Instructor, string Location, CourseFormat Format, DateTime StartDate, DateTime EndDate,
    decimal DurationHours, int Capacity, CourseStatus Status, int EnrolledCount, int CompletedCount);

public sealed record EmployeeResponse(int Id, string EmployeeNumber, string Name, string Email,
    string Department, string JobTitle, bool IsActive);

public sealed record EnrollmentResponse(int Id, int CourseId, int EmployeeId, string EmployeeName,
    string EmployeeNumber, string Department, string CourseTitle, EnrollmentStatus Status,
    int Progress, decimal? Score, DateTime RegisteredAt, DateTime? CompletedAt);

public sealed record DepartmentStatistics(string Department, int EmployeeCount, int EnrollmentCount,
    int CompletedCount, decimal TrainingHours);
public sealed record MonthlyStatistics(string Month, int Enrollments, int Completions);
public sealed record DashboardResponse(int EmployeeCount, int CourseCount, int ActiveCourseCount,
    int EnrollmentCount, int CompletedEnrollmentCount, decimal CompletionRate, decimal TotalTrainingHours,
    IReadOnlyList<DepartmentStatistics> DepartmentStats, IReadOnlyList<MonthlyStatistics> MonthlyStats);
