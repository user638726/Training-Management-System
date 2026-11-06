using System.ComponentModel.DataAnnotations;

namespace TrainingManagement.Api.Models;

public sealed class DepartmentRequest
{
    [Required, StringLength(80)] public string Name { get; set; } = "";
    public int? ParentId { get; set; }
    public int? ManagerUserId { get; set; }
    public string? RowVersion { get; set; }
}
public sealed class AccountUpdateRequest
{
    [Required] public string Role { get; set; } = "Employee";
    public bool IsActive { get; set; }
    public bool IsApproved { get; set; }
    public int? EmployeeId { get; set; }
    [StringLength(300)] public string? ExternalSubject { get; set; }
    [Required] public string RowVersion { get; set; } = "";
}
public sealed class InvitationRequest
{
    [Required, EmailAddress, StringLength(200)] public string Email { get; set; } = "";
    [Required] public string Role { get; set; } = "Employee";
    public int? EmployeeId { get; set; }
}
public sealed class AcceptInvitationRequest
{
    [Required, StringLength(200)] public string Token { get; set; } = "";
    [Required, StringLength(80, MinimumLength = 3), RegularExpression(@"^[A-Za-z0-9._-]+$")] public string Username { get; set; } = "";
    [Required, StringLength(100)] public string DisplayName { get; set; } = "";
    [Required, StringLength(256, MinimumLength = 12)] public string Password { get; set; } = "";
}
public sealed class CourseSettingsRequest
{
    public int? InstructorUserId { get; set; }
    public bool RequiresApproval { get; set; }
    public bool RequiresAttendance { get; set; }
    public bool RequiresQuiz { get; set; }
    [Range(0, 100)] public decimal PassScore { get; set; } = 60;
    [Range(typeof(decimal), "0", "100000000")] public decimal Cost { get; set; }
    public int? PrerequisiteCourseId { get; set; }
    [Range(1, 120)] public int? CertificateValidityMonths { get; set; }
    [Required] public string RowVersion { get; set; } = "";
}
public sealed class PlanRequest
{
    [Required, StringLength(160)] public string Title { get; set; } = "";
    [StringLength(80)] public string? Department { get; set; }
    [StringLength(100)] public string? JobTitle { get; set; }
    [Range(1, 3650)] public int? NewHireDays { get; set; }
    [Range(1, 3650)] public int DueDays { get; set; } = 30;
    [Range(0, 120)] public int RecurrenceMonths { get; set; }
    public bool IsActive { get; set; } = true;
    [Required, MinLength(1), MaxLength(100)] public int[] CourseIds { get; set; } = [];
    public string? RowVersion { get; set; }
}
public sealed class ApprovalDecision
{
    public bool Approve { get; set; }
    [StringLength(1000)] public string Comment { get; set; } = "";
    [Required] public string RowVersion { get; set; } = "";
}
public sealed class QuestionRequest
{
    [Required, StringLength(1000)] public string Prompt { get; set; } = "";
    [Required, MinLength(2), MaxLength(6)] public string[] Options { get; set; } = [];
    [Range(0, 5)] public int CorrectOption { get; set; }
}
public sealed class QuizRequest
{
    [Required, MinLength(1), MaxLength(100)] public QuestionRequest[] Questions { get; set; } = [];
    [Required] public string RowVersion { get; set; } = "";
}
public sealed class QuizSubmission
{
    [Required, MaxLength(100)] public Dictionary<int, int> Answers { get; set; } = [];
    [Range(1, int.MaxValue)] public int Revision { get; set; }
}
public sealed class AttendanceRequest
{
    public bool Attended { get; set; }
    [Required] public string RowVersion { get; set; } = "";
}
public sealed class SurveyRequest
{
    [Range(1, 5)] public int Rating { get; set; }
    [StringLength(2000)] public string Feedback { get; set; } = "";
}
public sealed class ImportRequest
{
    [Required, StringLength(2000000)] public string Csv { get; set; } = "";
}
public sealed record ImportRow(int Row, string Action, EmployeeRequest Employee, string? RowVersion, string[] Errors);
public sealed class ScheduleRequest
{
    [Required, StringLength(160)] public string Title { get; set; } = "";
    [StringLength(80)] public string? Department { get; set; }
    [Required, RegularExpression("^(Weekly|Monthly)$")] public string Frequency { get; set; } = "Weekly";
}
public sealed class PasswordChangeRequest
{
    [Required, StringLength(256)] public string CurrentPassword { get; set; } = "";
    [Required, StringLength(256, MinimumLength = 12)] public string NewPassword { get; set; } = "";
}
