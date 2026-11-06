using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TrainingManagement.Api.Models;
namespace TrainingManagement.Api.Data;
public sealed partial class TrainingDbContext
{
    public DbSet<ApprovalPolicy> ApprovalPolicies => Set<ApprovalPolicy>();
    public DbSet<ApprovalDelegation> Delegations => Set<ApprovalDelegation>();
    public DbSet<ApprovalWorkflow> Workflows => Set<ApprovalWorkflow>();
    public DbSet<ApprovalStage> ApprovalStages => Set<ApprovalStage>();
    public DbSet<CourseSeries> CourseSeries => Set<CourseSeries>();
    public DbSet<CourseMeeting> Meetings => Set<CourseMeeting>();
    public DbSet<WaitlistEntry> Waitlist => Set<WaitlistEntry>();
    public DbSet<TrainingBudget> Budgets => Set<TrainingBudget>();
    public DbSet<BudgetCommitment> Commitments => Set<BudgetCommitment>();
    public DbSet<ExpenseClaim> Expenses => Set<ExpenseClaim>();
    public DbSet<Skill> Skills => Set<Skill>();
    public DbSet<JobSkillRequirement> JobSkillRequirements => Set<JobSkillRequirement>();
    public DbSet<SkillCourse> SkillCourses => Set<SkillCourse>();
    public DbSet<SkillAssessment> SkillAssessments => Set<SkillAssessment>();
    public DbSet<DevelopmentGoal> Goals => Set<DevelopmentGoal>();
    public DbSet<CredentialType> CredentialTypes => Set<CredentialType>();
    public DbSet<CredentialRequirement> CredentialRequirements => Set<CredentialRequirement>();
    public DbSet<ExternalCredential> ExternalCredentials => Set<ExternalCredential>();
    public DbSet<IntegrationClient> IntegrationClients => Set<IntegrationClient>();
    public DbSet<ProvisioningGroup> ProvisioningGroups => Set<ProvisioningGroup>();
    public DbSet<ProvisioningLog> ProvisioningLogs => Set<ProvisioningLog>();
    public DbSet<HrSyncSchedule> HrSyncSchedules => Set<HrSyncSchedule>();
    public DbSet<LearningActivity> Activities => Set<LearningActivity>();
    public DbSet<LearningRecord> LearningRecords => Set<LearningRecord>();
    public DbSet<WebhookSubscription> WebhookSubscriptions => Set<WebhookSubscription>();
    public DbSet<IntegrationEvent> IntegrationEvents => Set<IntegrationEvent>();
    public DbSet<WebhookDelivery> WebhookDeliveries => Set<WebhookDelivery>();
    public DbSet<WebhookAttempt> WebhookAttempts => Set<WebhookAttempt>();

    private static void ConfigureAdvanced(ModelBuilder m)
    {
        Type[] types = [typeof(ApprovalPolicy),typeof(ApprovalDelegation),typeof(ApprovalWorkflow),typeof(ApprovalStage),typeof(CourseSeries),typeof(CourseMeeting),typeof(WaitlistEntry),typeof(TrainingBudget),typeof(BudgetCommitment),typeof(ExpenseClaim),typeof(Skill),typeof(JobSkillRequirement),typeof(SkillCourse),typeof(SkillAssessment),typeof(DevelopmentGoal),typeof(CredentialType),typeof(CredentialRequirement),typeof(ExternalCredential),typeof(IntegrationClient),typeof(ProvisioningGroup),typeof(ProvisioningLog),typeof(HrSyncSchedule),typeof(LearningActivity),typeof(LearningRecord),typeof(WebhookSubscription),typeof(IntegrationEvent),typeof(WebhookDelivery),typeof(WebhookAttempt)];
        foreach (var type in types) {
            var entity = m.Entity(type);
            foreach (var p in type.GetProperties()) {
                if (p.PropertyType == typeof(string) && !p.Name.EndsWith("Json")) entity.Property(p.Name).HasMaxLength(p.Name is "Comment" or "Description" or "Evidence" or "Summary" or "LastSummary" ? 2000 : p.Name.Contains("Url") || p.Name.Contains("Iri") || p.Name == "Endpoint" ? 1000 : p.Name == "ProtectedSecret" ? 2000 : 200);
                if (p.PropertyType == typeof(decimal) || p.PropertyType == typeof(decimal?)) entity.Property(p.Name).HasPrecision(12,2);
            }
        }
        m.Entity<AppUser>().Property(x => x.ProvisioningExternalId).HasMaxLength(200);
        m.Entity<AppUser>().HasIndex(x => new { x.ProvisioningClientId, x.ProvisioningExternalId }).IsUnique().HasFilter("[ProvisioningExternalId] IS NOT NULL");
        m.Entity<AppUser>().HasOne<IntegrationClient>().WithMany().HasForeignKey(x => x.ProvisioningClientId).OnDelete(DeleteBehavior.Restrict);
        m.Entity<Course>().HasOne<CourseSeries>().WithMany().HasForeignKey(x => x.SeriesId).OnDelete(DeleteBehavior.Restrict);
        m.Entity<Course>().HasOne<ApprovalPolicy>().WithMany().HasForeignKey(x => x.ApprovalPolicyId).OnDelete(DeleteBehavior.Restrict);
        m.Entity<Course>().HasOne<TrainingBudget>().WithMany().HasForeignKey(x => x.BudgetId).OnDelete(DeleteBehavior.Restrict);
        m.Entity<EnrollmentApproval>().Property(x => x.RequestedCost).HasPrecision(12,2);
        m.Entity<EnrollmentApproval>().HasOne<ApprovalWorkflow>().WithMany().HasForeignKey(x => x.WorkflowId).OnDelete(DeleteBehavior.Restrict);
        m.Entity<EnrollmentApproval>().HasOne<WaitlistEntry>().WithMany().HasForeignKey(x => x.WaitlistEntryId).OnDelete(DeleteBehavior.Restrict);
        m.Entity<ApprovalWorkflow>().HasOne(x => x.Employee).WithMany().OnDelete(DeleteBehavior.Restrict);
        m.Entity<ApprovalWorkflow>().HasOne<ApprovalPolicy>().WithMany().HasForeignKey(x => x.PolicyId).OnDelete(DeleteBehavior.Restrict);
        m.Entity<ApprovalWorkflow>().HasMany(x => x.Stages).WithOne(x => x.Workflow).HasForeignKey(x => x.WorkflowId).OnDelete(DeleteBehavior.Cascade);
        m.Entity<ApprovalStage>().HasIndex(x => new { x.WorkflowId, x.Position }).IsUnique();
        m.Entity<ApprovalDelegation>().HasOne<AppUser>().WithMany().HasForeignKey(x => x.PrincipalUserId).OnDelete(DeleteBehavior.Restrict);
        m.Entity<ApprovalDelegation>().HasOne<AppUser>().WithMany().HasForeignKey(x => x.DelegateUserId).OnDelete(DeleteBehavior.Restrict);
        m.Entity<CourseMeeting>().HasOne<Course>().WithMany().HasForeignKey(x => x.CourseId).OnDelete(DeleteBehavior.Restrict);
        m.Entity<WaitlistEntry>().HasOne(x => x.Course).WithMany().OnDelete(DeleteBehavior.Restrict);
        m.Entity<WaitlistEntry>().HasOne(x => x.Employee).WithMany().OnDelete(DeleteBehavior.Restrict);
        m.Entity<WaitlistEntry>().HasIndex(x => new { x.CourseId, x.EmployeeId }).IsUnique().HasFilter("[Status] IN ('Waiting','Offered')");
        m.Entity<TrainingBudget>().Property(x => x.Currency).HasMaxLength(3);
        m.Entity<TrainingBudget>().HasOne<AppUser>().WithMany().HasForeignKey(x => x.OwnerUserId).OnDelete(DeleteBehavior.Restrict);
        m.Entity<TrainingBudget>().HasOne<ApprovalPolicy>().WithMany().HasForeignKey(x => x.ExpensePolicyId).OnDelete(DeleteBehavior.Restrict);
        m.Entity<BudgetCommitment>().HasOne<TrainingBudget>().WithMany().HasForeignKey(x => x.BudgetId).OnDelete(DeleteBehavior.Restrict);
        m.Entity<BudgetCommitment>().HasOne(x => x.Enrollment).WithMany().HasForeignKey(x => x.EnrollmentId).OnDelete(DeleteBehavior.Restrict);
        m.Entity<BudgetCommitment>().HasIndex(x => new { x.EnrollmentId, x.Attempt }).IsUnique();
        m.Entity<ExpenseClaim>().HasOne(x => x.Employee).WithMany().OnDelete(DeleteBehavior.Restrict);
        m.Entity<ExpenseClaim>().HasOne(x => x.Budget).WithMany().OnDelete(DeleteBehavior.Restrict);
        m.Entity<ExpenseClaim>().HasOne<Enrollment>().WithMany().HasForeignKey(x => x.EnrollmentId).OnDelete(DeleteBehavior.Restrict);
        m.Entity<ExpenseClaim>().HasOne<ApprovalWorkflow>().WithMany().HasForeignKey(x => x.WorkflowId).OnDelete(DeleteBehavior.Restrict);
        m.Entity<Skill>().HasIndex(x => x.Name).IsUnique();
        m.Entity<JobSkillRequirement>().HasOne(x => x.Skill).WithMany().OnDelete(DeleteBehavior.Restrict);
        m.Entity<JobSkillRequirement>().HasIndex(x => new { x.JobTitle, x.SkillId }).IsUnique();
        m.Entity<SkillCourse>().HasKey(x => new { x.SkillId, x.CourseId });
        m.Entity<SkillCourse>().HasOne<Skill>().WithMany().HasForeignKey(x => x.SkillId).OnDelete(DeleteBehavior.Restrict);
        m.Entity<SkillCourse>().HasOne<Course>().WithMany().HasForeignKey(x => x.CourseId).OnDelete(DeleteBehavior.Restrict);
        m.Entity<SkillAssessment>().HasOne(x => x.Employee).WithMany().OnDelete(DeleteBehavior.Restrict);
        m.Entity<SkillAssessment>().HasOne(x => x.Skill).WithMany().OnDelete(DeleteBehavior.Restrict);
        m.Entity<DevelopmentGoal>().HasOne(x => x.Employee).WithMany().OnDelete(DeleteBehavior.Restrict);
        m.Entity<DevelopmentGoal>().HasOne(x => x.Skill).WithMany().OnDelete(DeleteBehavior.Restrict);
        m.Entity<CredentialType>().HasIndex(x => x.Name).IsUnique();
        m.Entity<CredentialRequirement>().HasOne(x => x.CredentialType).WithMany().OnDelete(DeleteBehavior.Restrict);
        m.Entity<CredentialRequirement>().HasIndex(x => new { x.JobTitle, x.CredentialTypeId }).IsUnique();
        m.Entity<ExternalCredential>().HasOne(x => x.Employee).WithMany().OnDelete(DeleteBehavior.Restrict);
        m.Entity<ExternalCredential>().HasOne(x => x.CredentialType).WithMany().OnDelete(DeleteBehavior.Restrict);
        m.Entity<ExternalCredential>().HasOne<ExternalCredential>().WithMany().HasForeignKey(x => x.ReplacesId).OnDelete(DeleteBehavior.Restrict);
        m.Entity<ExternalCredential>().HasIndex(x => new { x.CredentialTypeId, x.CredentialNumber });
        m.Entity<IntegrationClient>().Property(x => x.TokenHash).HasMaxLength(64);
        m.Entity<IntegrationClient>().Property(x => x.Scopes).HasMaxLength(1000);
        m.Entity<IntegrationClient>().HasIndex(x => x.TokenHash).IsUnique();
        m.Entity<ProvisioningGroup>().HasIndex(x => new { x.ClientId, x.ExternalId }).IsUnique();
        m.Entity<ProvisioningGroup>().HasOne<IntegrationClient>().WithMany().HasForeignKey(x => x.ClientId).OnDelete(DeleteBehavior.Restrict);
        m.Entity<LearningActivity>().Property(x => x.ActivityIri).HasMaxLength(450);
        m.Entity<LearningActivity>().HasIndex(x => x.ActivityIri).IsUnique();
        m.Entity<LearningActivity>().HasOne(x => x.Course).WithMany().OnDelete(DeleteBehavior.Restrict);
        m.Entity<LearningActivity>().HasOne<IntegrationClient>().WithMany().HasForeignKey(x => x.ClientId).OnDelete(DeleteBehavior.Restrict);
        m.Entity<LearningRecord>().HasOne(x => x.Employee).WithMany().OnDelete(DeleteBehavior.Restrict);
        m.Entity<LearningRecord>().HasOne(x => x.Activity).WithMany().OnDelete(DeleteBehavior.Restrict);
        m.Entity<LearningRecord>().HasIndex(x => new { x.EmployeeId, x.ActivityId, x.StoredAt });
        m.Entity<HrSyncSchedule>().HasIndex(x=>x.IsActive).IsUnique().HasFilter("[IsActive] = 1");
        m.Entity<WebhookSubscription>().Property(x => x.EventTypes).HasMaxLength(1000);
        m.Entity<WebhookDelivery>().HasOne(x => x.Event).WithMany().OnDelete(DeleteBehavior.Restrict);
        m.Entity<WebhookDelivery>().HasOne(x => x.Subscription).WithMany().OnDelete(DeleteBehavior.Restrict);
        m.Entity<WebhookDelivery>().HasIndex(x => new { x.EventId, x.SubscriptionId }).IsUnique();
        m.Entity<WebhookDelivery>().HasIndex(x => new { x.Status, x.NextAttemptAt });
        m.Entity<WebhookAttempt>().HasOne<WebhookDelivery>().WithMany().HasForeignKey(x => x.DeliveryId).OnDelete(DeleteBehavior.Restrict);
    }
    private void CaptureIntegrationEvents(IEnumerable<(Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry Entry, EntityState State, string? Before)> changes)
    {
        foreach (var change in changes) {
            string? type = null; object? payload = null;
            if (change.Entry.Entity is Course c) { var old = change.Before is null ? null : JsonDocument.Parse(change.Before);
                var beforeStatus = old?.RootElement.TryGetProperty("Status", out var s) == true ? s.GetInt32() : (int?)null;
                type = c.Status == CourseStatus.Published && beforeStatus != (int)CourseStatus.Published ? "course.published" : "course.updated";
                payload = new { courseId = c.Id, c.Revision, status = c.Status.ToString() }; old?.Dispose(); }
            if (change.Entry.Entity is Enrollment e) {
                using var old = change.Before is null ? null : JsonDocument.Parse(change.Before);
                var beforeStatus = old?.RootElement.TryGetProperty("Status", out var s) == true ? s.GetInt32() : (int?)null;
                if (beforeStatus != (int)e.Status) type = e.Status switch { EnrollmentStatus.Completed => "enrollment.completed", EnrollmentStatus.Cancelled => "enrollment.cancelled", EnrollmentStatus.Registered => "enrollment.registered", _ => null };
                payload = new { enrollmentId = e.Id, e.CourseId, e.EmployeeId, e.Attempt, status = e.Status.ToString() };
            }
            if (change.Entry.Entity is EnrollmentApproval a && a.Status == "Approved") { type = "enrollment.approved"; payload = new { approvalId = a.Id, a.CourseId, a.EmployeeId }; }
            if (type is not null && change.State != EntityState.Deleted) IntegrationEvents.Add(new IntegrationEvent { Type = type, PayloadJson = JsonSerializer.Serialize(payload) });
        }
    }
}
