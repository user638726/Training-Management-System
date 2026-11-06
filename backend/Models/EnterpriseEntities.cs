using System.ComponentModel.DataAnnotations;

namespace TrainingManagement.Api.Models;

public static class Roles
{
    public const string Administrators = "Admin,HR";
    public const string Reviewers = "Admin,HR,Manager";
    public const string Teachers = "Admin,HR,Instructor";
    public static readonly string[] All = ["Admin", "HR", "Manager", "Instructor", "Employee"];
}

public sealed partial class AppUser
{
    public bool IsApproved { get; set; } = true;
    public string? ExternalSubject { get; set; }
    public string? ExternalIssuer { get; set; }
    [Timestamp] public byte[] RowVersion { get; set; } = [];
}
public sealed partial class Employee
{
    public DateTime HiredAt { get; set; } = DateTime.UtcNow.Date;
    [Timestamp] public byte[] RowVersion { get; set; } = [];
}
public sealed partial class Course
{
    public int Revision { get; set; } = 1;
    public int QuizRevision { get; set; } = 1;
    public int? InstructorUserId { get; set; }
    public bool RequiresApproval { get; set; }
    public bool RequiresAttendance { get; set; }
    public bool RequiresQuiz { get; set; }
    public decimal PassScore { get; set; } = 60;
    public decimal Cost { get; set; }
    public int? PrerequisiteCourseId { get; set; }
    public int? CertificateValidityMonths { get; set; }
    [Timestamp] public byte[] RowVersion { get; set; } = [];
}
public sealed partial class Enrollment
{
    public int Attempt { get; set; } = 1;
    public bool Attended { get; set; }
    public decimal? RecognizedHours { get; set; }
    public decimal? RecognizedCost { get; set; }
    public string? CourseTitleSnapshot { get; set; }
    public string? DepartmentSnapshot { get; set; }
    public int? CourseRevisionSnapshot { get; set; }
    [Timestamp] public byte[] RowVersion { get; set; } = [];
}
public sealed class Department
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int? ParentId { get; set; }
    public int? ManagerUserId { get; set; }
    [Timestamp] public byte[] RowVersion { get; set; } = [];
}
public sealed class AuditEvent
{
    public long Id { get; set; }
    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
    public int? ActorId { get; set; }
    public string ActorName { get; set; } = "system";
    public string Action { get; set; } = "";
    public string EntityType { get; set; } = "";
    public string EntityId { get; set; } = "";
    public string? BeforeJson { get; set; }
    public string? AfterJson { get; set; }
    public string TraceId { get; set; } = "";
}
public sealed class EnrollmentHistory
{
    public int Id { get; set; }
    public int EnrollmentId { get; set; }
    public int Attempt { get; set; }
    public string SnapshotJson { get; set; } = "";
    public DateTime ArchivedAt { get; set; } = DateTime.UtcNow;
}
public sealed partial class EnrollmentApproval
{
    public int Id { get; set; }
    public int CourseId { get; set; }
    public Course Course { get; set; } = null!;
    public int EmployeeId { get; set; }
    public Employee Employee { get; set; } = null!;
    public string Status { get; set; } = "Pending";
    public DateTime RequestedAt { get; set; } = DateTime.UtcNow;
    public int? ReviewerId { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public string Comment { get; set; } = "";
    [Timestamp] public byte[] RowVersion { get; set; } = [];
}
public sealed class LearningPlan
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string? Department { get; set; }
    public string? JobTitle { get; set; }
    public int? NewHireDays { get; set; }
    public int DueDays { get; set; } = 30;
    public int RecurrenceMonths { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public ICollection<LearningPlanCourse> Courses { get; set; } = new List<LearningPlanCourse>();
    [Timestamp] public byte[] RowVersion { get; set; } = [];
}
public sealed class LearningPlanCourse
{
    public int LearningPlanId { get; set; }
    public int CourseId { get; set; }
}
public sealed class LearningAssignment
{
    public int Id { get; set; }
    public int LearningPlanId { get; set; }
    public LearningPlan LearningPlan { get; set; } = null!;
    public int CourseId { get; set; }
    public Course Course { get; set; } = null!;
    public int EmployeeId { get; set; }
    public Employee Employee { get; set; } = null!;
    public string Cycle { get; set; } = "once";
    public DateTime AssignedAt { get; set; } = DateTime.UtcNow;
    public DateTime DueAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}
public sealed class Notification
{
    public long Id { get; set; }
    public int UserId { get; set; }
    public string Title { get; set; } = "";
    public string Body { get; set; } = "";
    public string Link { get; set; } = "/learning";
    public string DedupeKey { get; set; } = Guid.NewGuid().ToString("N");
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ReadAt { get; set; }
    public string DeliveryStatus { get; set; } = "InApp";
    public int DeliveryAttempts { get; set; }
    public DateTime? NextDeliveryAt { get; set; }
}
public sealed class CourseMaterial
{
    public int Id { get; set; }
    public int CourseId { get; set; }
    public string FileName { get; set; } = "";
    public string ContentType { get; set; } = "";
    public byte[] Content { get; set; } = [];
    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
}
public sealed class QuizQuestion
{
    public int Id { get; set; }
    public int CourseId { get; set; }
    public int Revision { get; set; }
    public string Prompt { get; set; } = "";
    public string OptionsJson { get; set; } = "[]";
    public int CorrectOption { get; set; }
}
public sealed class QuizAttempt
{
    public int Id { get; set; }
    public int EnrollmentId { get; set; }
    public int EnrollmentAttempt { get; set; }
    public int CourseRevision { get; set; }
    public decimal Score { get; set; }
    public string AnswersJson { get; set; } = "";
    public DateTime SubmittedAt { get; set; } = DateTime.UtcNow;
}
public sealed class CourseSurvey
{
    public int Id { get; set; }
    public int EnrollmentId { get; set; }
    public int Attempt { get; set; }
    public int Rating { get; set; }
    public string Feedback { get; set; } = "";
    public DateTime SubmittedAt { get; set; } = DateTime.UtcNow;
}
public sealed class Certificate
{
    public int Id { get; set; }
    public int EnrollmentId { get; set; }
    public int Attempt { get; set; }
    public int EmployeeId { get; set; }
    public Employee Employee { get; set; } = null!;
    public string Serial { get; set; } = Guid.NewGuid().ToString("N").ToUpperInvariant();
    public string EmployeeName { get; set; } = "";
    public string CourseTitle { get; set; } = "";
    public decimal Hours { get; set; }
    public DateTime IssuedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }
}
public sealed class AccountInvitation
{
    public int Id { get; set; }
    public string TokenHash { get; set; } = "";
    public string Email { get; set; } = "";
    public string Role { get; set; } = "Employee";
    public int? EmployeeId { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? AcceptedAt { get; set; }
}
public sealed class ImportBatch
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int CreatedBy { get; set; }
    public string RowsJson { get; set; } = "";
    public DateTime ExpiresAt { get; set; } = DateTime.UtcNow.AddMinutes(30);
    public DateTime? CommittedAt { get; set; }
}
public sealed class ReportSchedule
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string Title { get; set; } = "";
    public string? Department { get; set; }
    public string Frequency { get; set; } = "Weekly";
    public DateTime NextRunAt { get; set; } = DateTime.UtcNow;
    public bool IsActive { get; set; } = true;
}
public sealed class ReportDelivery
{
    public long Id { get; set; }
    public int UserId { get; set; }
    public string Title { get; set; } = "";
    public int PermissionVersion { get; set; }
    public string ScopeFingerprint { get; set; } = "";
    public byte[] Content { get; set; } = [];
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
public sealed class SsoTicket
{
    public string Hash { get; set; } = "";
    public int UserId { get; set; }
    public DateTime ExpiresAt { get; set; }
}
