using System.ComponentModel.DataAnnotations;
namespace TrainingManagement.Api.Models;

public sealed partial class Course
{
    public int? SeriesId { get; set; }
    public int? ApprovalPolicyId { get; set; }
    public int? BudgetId { get; set; }
    public bool CheckScheduleConflicts { get; set; }
}
public sealed partial class EnrollmentApproval
{
    public int? WorkflowId { get; set; }
    public int? WaitlistEntryId { get; set; }
    public decimal RequestedCost { get; set; }
}
public sealed class ApprovalPolicy
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public decimal MinimumAmount { get; set; }
    public string StagesJson { get; set; } = "[]";
    public bool IsActive { get; set; } = true;
    [Timestamp] public byte[] RowVersion { get; set; } = [];
}
public sealed class ApprovalDelegation
{
    public int Id { get; set; }
    public int PrincipalUserId { get; set; }
    public int DelegateUserId { get; set; }
    public DateTime StartsAt { get; set; }
    public DateTime EndsAt { get; set; }
    public bool IsActive { get; set; } = true;
    public string Reason { get; set; } = "";
    [Timestamp] public byte[] RowVersion { get; set; } = [];
}
public sealed class ApprovalWorkflow
{
    public int Id { get; set; }
    public string Kind { get; set; } = "Enrollment";
    public int RelatedId { get; set; }
    public int EmployeeId { get; set; }
    public Employee Employee { get; set; } = null!;
    public int PolicyId { get; set; }
    public string Title { get; set; } = "";
    public decimal Amount { get; set; }
    public string Status { get; set; } = "Pending";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    public ICollection<ApprovalStage> Stages { get; set; } = new List<ApprovalStage>();
    [Timestamp] public byte[] RowVersion { get; set; } = [];
}
public sealed class ApprovalStage
{
    public int Id { get; set; }
    public int WorkflowId { get; set; }
    public ApprovalWorkflow Workflow { get; set; } = null!;
    public int Position { get; set; }
    public string Name { get; set; } = "";
    public string ApproverKind { get; set; } = "Manager";
    public int? ApproverUserId { get; set; }
    public string Status { get; set; } = "Waiting";
    public int DueHours { get; set; } = 48;
    public DateTime? DueAt { get; set; }
    public DateTime? ActedAt { get; set; }
    public int? ActorUserId { get; set; }
    public int? RepresentedUserId { get; set; }
    public string Comment { get; set; } = "";
    [Timestamp] public byte[] RowVersion { get; set; } = [];
}
public sealed class CourseSeries
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
}
public sealed class CourseMeeting
{
    public int Id { get; set; }
    public int CourseId { get; set; }
    public DateTime StartsAt { get; set; }
    public DateTime EndsAt { get; set; }
    public string Location { get; set; } = "";
}
public sealed class WaitlistEntry
{
    public int Id { get; set; }
    public int CourseId { get; set; }
    public Course Course { get; set; } = null!;
    public int EmployeeId { get; set; }
    public Employee Employee { get; set; } = null!;
    public string Status { get; set; } = "Waiting";
    public DateTime QueuedAt { get; set; } = DateTime.UtcNow;
    public DateTime? OfferedAt { get; set; }
    public DateTime? OfferExpiresAt { get; set; }
    [Timestamp] public byte[] RowVersion { get; set; } = [];
}
public sealed class TrainingBudget
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int Year { get; set; }
    public string? Department { get; set; }
    public int OwnerUserId { get; set; }
    public decimal Allocated { get; set; }
    public string Currency { get; set; } = "TWD";
    public int? ExpensePolicyId { get; set; }
    public bool IsActive { get; set; } = true;
    [Timestamp] public byte[] RowVersion { get; set; } = [];
}
public sealed class BudgetCommitment
{
    public int Id { get; set; }
    public int BudgetId { get; set; }
    public int EnrollmentId { get; set; }
    public Enrollment Enrollment { get; set; } = null!;
    public int Attempt { get; set; }
    public decimal Amount { get; set; }
    public string Status { get; set; } = "Reserved";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
public sealed class ExpenseClaim
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }
    public Employee Employee { get; set; } = null!;
    public int BudgetId { get; set; }
    public TrainingBudget Budget { get; set; } = null!;
    public int? EnrollmentId { get; set; }
    public string Title { get; set; } = "";
    public string Vendor { get; set; } = "";
    public decimal Amount { get; set; }
    public string Status { get; set; } = "Submitted";
    public int? WorkflowId { get; set; }
    public string ReceiptName { get; set; } = "";
    public string ReceiptType { get; set; } = "";
    public byte[] Receipt { get; set; } = [];
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ReviewedAt { get; set; }
    public int? ReviewerId { get; set; }
    public string Comment { get; set; } = "";
    [Timestamp] public byte[] RowVersion { get; set; } = [];
}
public sealed class Skill
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Category { get; set; } = "";
    public string Description { get; set; } = "";
}
public sealed class JobSkillRequirement
{
    public int Id { get; set; }
    public string JobTitle { get; set; } = "";
    public int SkillId { get; set; }
    public Skill Skill { get; set; } = null!;
    public int TargetLevel { get; set; }
}
public sealed class SkillCourse
{
    public int SkillId { get; set; }
    public int CourseId { get; set; }
    public int Level { get; set; }
}
public sealed class SkillAssessment
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }
    public Employee Employee { get; set; } = null!;
    public int SkillId { get; set; }
    public Skill Skill { get; set; } = null!;
    public int Level { get; set; }
    public string Evidence { get; set; } = "";
    public string Status { get; set; } = "Pending";
    public int? ReviewerId { get; set; }
    public string Comment { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ReviewedAt { get; set; }
    [Timestamp] public byte[] RowVersion { get; set; } = [];
}
public sealed class DevelopmentGoal
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }
    public Employee Employee { get; set; } = null!;
    public string Title { get; set; } = "";
    public int SkillId { get; set; }
    public Skill Skill { get; set; } = null!;
    public int TargetLevel { get; set; }
    public DateTime DueAt { get; set; }
    public string Status { get; set; } = "Active";
    public string CourseIdsJson { get; set; } = "[]";
    [Timestamp] public byte[] RowVersion { get; set; } = [];
}
public sealed class CredentialType
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
}
public sealed class CredentialRequirement
{
    public int Id { get; set; }
    public string JobTitle { get; set; } = "";
    public int CredentialTypeId { get; set; }
    public CredentialType CredentialType { get; set; } = null!;
    public int RenewalNoticeDays { get; set; } = 60;
}
public sealed class ExternalCredential
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }
    public Employee Employee { get; set; } = null!;
    public int CredentialTypeId { get; set; }
    public CredentialType CredentialType { get; set; } = null!;
    public string Issuer { get; set; } = "";
    public string CredentialNumber { get; set; } = "";
    public DateTime IssuedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public int? ReplacesId { get; set; }
    public string Status { get; set; } = "Pending";
    public string FileName { get; set; } = "";
    public string ContentType { get; set; } = "";
    public byte[] Content { get; set; } = [];
    public int? ReviewerId { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public string Comment { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    [Timestamp] public byte[] RowVersion { get; set; } = [];
}
public sealed class IntegrationClient
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string TokenHash { get; set; } = "";
    public string Scopes { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; }
    public DateTime? LastUsedAt { get; set; }
    [Timestamp] public byte[] RowVersion { get; set; } = [];
}
public sealed partial class AppUser
{
    public string? ProvisioningExternalId { get; set; }
    public int? ProvisioningClientId { get; set; }
}
public sealed class ProvisioningGroup
{
    public int Id { get; set; }
    public int ClientId { get; set; }
    public string ExternalId { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string? Department { get; set; }
    public string MembersJson { get; set; } = "[]";
    public DateTime ModifiedAt { get; set; } = DateTime.UtcNow;
}
public sealed class ProvisioningLog
{
    public long Id { get; set; }
    public int? ClientId { get; set; }
    public string Source { get; set; } = "SCIM";
    public string Action { get; set; } = "";
    public string ResourceId { get; set; } = "";
    public string Status { get; set; } = "Success";
    public string Summary { get; set; } = "";
    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
}
public sealed class HrSyncSchedule
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int IntervalMinutes { get; set; } = 60;
    public bool IsActive { get; set; } = true;
    public bool AllowDeactivation { get; set; }
    public DateTime NextRunAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastRunAt { get; set; }
    public string LastStatus { get; set; } = "Never";
    public string LastSummary { get; set; } = "";
}
public sealed class LearningActivity
{
    public int Id { get; set; }
    public int CourseId { get; set; }
    public Course Course { get; set; } = null!;
    public string Title { get; set; } = "";
    public string ActivityIri { get; set; } = "";
    public string LaunchUrl { get; set; } = "";
    public string Kind { get; set; } = "External";
    public int? ClientId { get; set; }
    public int RequiredSeconds { get; set; }
    public bool IsActive { get; set; } = true;
}
public sealed class LearningRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int EmployeeId { get; set; }
    public Employee Employee { get; set; } = null!;
    public int ActivityId { get; set; }
    public LearningActivity Activity { get; set; } = null!;
    public int? ClientId { get; set; }
    public string VerbIri { get; set; } = "";
    public string Source { get; set; } = "Manual";
    public decimal? Score { get; set; }
    public int DurationSeconds { get; set; }
    public decimal Progress { get; set; }
    public string StatementJson { get; set; } = "";
    public string StatementHash { get; set; } = "";
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public DateTime StoredAt { get; set; } = DateTime.UtcNow;
    public bool IsVoided { get; set; }
}
public sealed class WebhookSubscription
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Endpoint { get; set; } = "";
    public string EventTypes { get; set; } = "";
    public string ProtectedSecret { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    [Timestamp] public byte[] RowVersion { get; set; } = [];
}
public sealed class IntegrationEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Type { get; set; } = "";
    public string PayloadJson { get; set; } = "";
    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
    public DateTime? ExpandedAt { get; set; }
}
public sealed class WebhookDelivery
{
    public long Id { get; set; }
    public Guid EventId { get; set; }
    public IntegrationEvent Event { get; set; } = null!;
    public int SubscriptionId { get; set; }
    public WebhookSubscription Subscription { get; set; } = null!;
    public string Status { get; set; } = "Pending";
    public int Attempts { get; set; }
    public DateTime NextAttemptAt { get; set; } = DateTime.UtcNow;
    public DateTime? LeaseUntil { get; set; }
    public Guid? LeaseToken { get; set; }
    public int? LastHttpStatus { get; set; }
    public string LastError { get; set; } = "";
    [Timestamp] public byte[] RowVersion { get; set; } = [];
}
public sealed class WebhookAttempt
{
    public long Id { get; set; }
    public long DeliveryId { get; set; }
    public int? HttpStatus { get; set; }
    public string Outcome { get; set; } = "";
    public DateTime At { get; set; } = DateTime.UtcNow;
}
