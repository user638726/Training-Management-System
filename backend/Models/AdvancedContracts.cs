using System.ComponentModel.DataAnnotations;
namespace TrainingManagement.Api.Models;
public sealed class StageDefinition
{
    [Required,StringLength(100)] public string Name { get; set; } = "";
    [Required,RegularExpression("^(Manager|HR|User|BudgetOwner)$")] public string Kind { get; set; } = "Manager";
    public int? UserId { get; set; }
    [Range(1,720)] public int DueHours { get; set; } = 48;
}
public sealed class PolicyRequest
{
    [Required,StringLength(200)] public string Name { get; set; } = "";
    [Range(typeof(decimal),"0","100000000")] public decimal MinimumAmount { get; set; }
    [Required,MinLength(1),MaxLength(10)] public StageDefinition[] Stages { get; set; } = [];
    public bool IsActive { get; set; } = true;
    public string? RowVersion { get; set; }
}
public sealed class DelegationRequest
{
    [Range(1,int.MaxValue)] public int PrincipalUserId { get; set; }
    [Range(1,int.MaxValue)] public int DelegateUserId { get; set; }
    public DateTimeOffset StartsAt { get; set; }
    public DateTimeOffset EndsAt { get; set; }
    [Required,StringLength(1000)] public string Reason { get; set; } = "";
}
public sealed class AdvancedCourseRequest
{
    public int? SeriesId { get; set; }
    public int? PolicyId { get; set; }
    public int? BudgetId { get; set; }
    public bool CheckScheduleConflicts { get; set; }
    [Required] public string RowVersion { get; set; } = "";
}
public sealed class SeriesRequest
{
    [Required,StringLength(200)] public string Title { get; set; } = "";
    [StringLength(2000)] public string Description { get; set; } = "";
}
public sealed class OfferingRequest
{
    [Range(1,int.MaxValue)] public int SourceCourseId { get; set; }
    [Required,StringLength(160)] public string Title { get; set; } = "";
    public DateTimeOffset StartsAt { get; set; }
    public DateTimeOffset EndsAt { get; set; }
    [Range(1,10000)] public int Capacity { get; set; } = 30;
    [Required,StringLength(300)] public string Location { get; set; } = "";
}
public sealed class MeetingRequest
{
    public DateTimeOffset StartsAt { get; set; }
    public DateTimeOffset EndsAt { get; set; }
    [Required,StringLength(300)] public string Location { get; set; } = "";
}
public sealed class BudgetRequest
{
    [Required,StringLength(200)] public string Name { get; set; } = "";
    [Range(2000,2200)] public int Year { get; set; }
    [StringLength(80)] public string? Department { get; set; }
    [Range(1,int.MaxValue)] public int OwnerUserId { get; set; }
    [Range(typeof(decimal),"0","1000000000")] public decimal Allocated { get; set; }
    [Required,RegularExpression("^[A-Z]{3}$")] public string Currency { get; set; } = "TWD";
    public int? ExpensePolicyId { get; set; }
    public bool IsActive { get; set; } = true;
    public string? RowVersion { get; set; }
}
public sealed class ExpenseUpload
{
    [Range(1,int.MaxValue)] public int BudgetId { get; set; }
    public int? EnrollmentId { get; set; }
    [Required,StringLength(200)] public string Title { get; set; } = "";
    [Required,StringLength(200)] public string Vendor { get; set; } = "";
    [Range(typeof(decimal),"0.01","100000000")] public decimal Amount { get; set; }
    [Required] public IFormFile Receipt { get; set; } = null!;
}
public sealed class SkillRequest
{
    [Required,StringLength(200)] public string Name { get; set; } = "";
    [StringLength(200)] public string Category { get; set; } = "";
    [StringLength(2000)] public string Description { get; set; } = "";
}
public sealed class SkillRequirementRequest
{
    [Required,StringLength(100)] public string JobTitle { get; set; } = "";
    [Range(1,int.MaxValue)] public int SkillId { get; set; }
    [Range(1,5)] public int TargetLevel { get; set; }
}
public sealed class SkillCourseRequest
{
    [Range(1,int.MaxValue)] public int SkillId { get; set; }
    [Range(1,int.MaxValue)] public int CourseId { get; set; }
    [Range(1,5)] public int Level { get; set; }
}
public sealed class AssessmentRequest
{
    [Range(1,int.MaxValue)] public int SkillId { get; set; }
    [Range(1,5)] public int Level { get; set; }
    [Required,StringLength(2000)] public string Evidence { get; set; } = "";
}
public sealed class GoalRequest
{
    public int? EmployeeId { get; set; }
    [Required,StringLength(200)] public string Title { get; set; } = "";
    [Range(1,int.MaxValue)] public int SkillId { get; set; }
    [Range(1,5)] public int TargetLevel { get; set; }
    public DateTimeOffset DueAt { get; set; }
    [Required,RegularExpression("^(Active|Completed|Cancelled)$")] public string Status { get; set; } = "Active";
    [MaxLength(50)] public int[] CourseIds { get; set; } = [];
    public string? RowVersion { get; set; }
}
public sealed class CredentialRequirementRequest
{
    [Required,StringLength(100)] public string JobTitle { get; set; } = "";
    [Range(1,int.MaxValue)] public int CredentialTypeId { get; set; }
    [Range(1,365)] public int RenewalNoticeDays { get; set; } = 60;
}
public sealed class CredentialUpload
{
    [Range(1,int.MaxValue)] public int CredentialTypeId { get; set; }
    [Required,StringLength(200)] public string Issuer { get; set; } = "";
    [Required,StringLength(200)] public string CredentialNumber { get; set; } = "";
    public DateTimeOffset IssuedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public int? ReplacesId { get; set; }
    [Required] public IFormFile File { get; set; } = null!;
}
public sealed class ClientRequest
{
    [Required,StringLength(200)] public string Name { get; set; } = "";
    [Required,MinLength(1),MaxLength(10)] public string[] Scopes { get; set; } = [];
    [Range(1,365)] public int ValidDays { get; set; } = 90;
}
public sealed class ActivityRequest
{
    [Range(1,int.MaxValue)] public int CourseId { get; set; }
    [Required,StringLength(200)] public string Title { get; set; } = "";
    [Required,StringLength(450),Url] public string ActivityIri { get; set; } = "";
    [Required,StringLength(1000),Url] public string LaunchUrl { get; set; } = "";
    [Required,RegularExpression("^(External|Video)$")] public string Kind { get; set; } = "External";
    public int? ClientId { get; set; }
    [Range(0,86400)] public int RequiredSeconds { get; set; }
}
public sealed class ActivityProgressRequest
{
    [Range(0,86400)] public int DurationSeconds { get; set; }
    [Range(typeof(decimal),"0","100")] public decimal Progress { get; set; }
}
public sealed class WebhookRequest
{
    [Required,StringLength(200)] public string Name { get; set; } = "";
    [Required,StringLength(1000),Url] public string Endpoint { get; set; } = "";
    [Required,MinLength(1),MaxLength(10)] public string[] Events { get; set; } = [];
}
public sealed class HrScheduleRequest
{
    [Required,StringLength(200)] public string Name { get; set; } = "";
    [Range(15,10080)] public int IntervalMinutes { get; set; } = 60;
    public bool AllowDeactivation { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed record ImportPreviewResult(Guid Id,DateTime ExpiresAt,bool Valid,ImportRow[] Rows);
