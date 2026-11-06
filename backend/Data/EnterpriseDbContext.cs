using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using TrainingManagement.Api.Models;

namespace TrainingManagement.Api.Data;

public sealed partial class TrainingDbContext
{
    public DbSet<Department> Departments => Set<Department>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();
    public DbSet<EnrollmentHistory> EnrollmentHistories => Set<EnrollmentHistory>();
    public DbSet<EnrollmentApproval> Approvals => Set<EnrollmentApproval>();
    public DbSet<LearningPlan> LearningPlans => Set<LearningPlan>();
    public DbSet<LearningAssignment> Assignments => Set<LearningAssignment>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<CourseMaterial> Materials => Set<CourseMaterial>();
    public DbSet<QuizQuestion> QuizQuestions => Set<QuizQuestion>();
    public DbSet<QuizAttempt> QuizAttempts => Set<QuizAttempt>();
    public DbSet<CourseSurvey> Surveys => Set<CourseSurvey>();
    public DbSet<Certificate> Certificates => Set<Certificate>();
    public DbSet<AccountInvitation> Invitations => Set<AccountInvitation>();
    public DbSet<ImportBatch> ImportBatches => Set<ImportBatch>();
    public DbSet<ReportSchedule> ReportSchedules => Set<ReportSchedule>();
    public DbSet<ReportDelivery> ReportDeliveries => Set<ReportDelivery>();
    public DbSet<SsoTicket> SsoTickets => Set<SsoTicket>();

    private static void ConfigureEnterprise(ModelBuilder m)
    {
        ConfigureAdvanced(m);
        m.Entity<AppUser>().Property(x => x.ExternalSubject).HasMaxLength(300);
        m.Entity<AppUser>().Property(x => x.ExternalIssuer).HasMaxLength(500);
        m.Entity<AppUser>().HasIndex(x => x.ExternalSubject).IsUnique().HasFilter("[ExternalSubject] IS NOT NULL");
        m.Entity<Department>(e => {
            e.Property(x => x.Name).HasMaxLength(80); e.HasIndex(x => x.Name).IsUnique();
            e.HasOne<Department>().WithMany().HasForeignKey(x => x.ParentId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.ManagerUserId).OnDelete(DeleteBehavior.Restrict);
        });
        m.Entity<Course>().Property(x => x.Cost).HasPrecision(12, 2);
        m.Entity<Course>().Property(x => x.PassScore).HasPrecision(5, 2);
        m.Entity<Course>().HasOne<AppUser>().WithMany().HasForeignKey(x => x.InstructorUserId).OnDelete(DeleteBehavior.Restrict);
        m.Entity<Course>().HasOne<Course>().WithMany().HasForeignKey(x => x.PrerequisiteCourseId).OnDelete(DeleteBehavior.Restrict);
        m.Entity<Enrollment>().Property(x => x.RecognizedHours).HasPrecision(8, 2);
        m.Entity<Enrollment>().Property(x => x.RecognizedCost).HasPrecision(12, 2);
        m.Entity<Enrollment>().Property(x => x.CourseTitleSnapshot).HasMaxLength(160);
        m.Entity<Enrollment>().Property(x => x.DepartmentSnapshot).HasMaxLength(80);
        m.Entity<AuditEvent>(e => {
            e.Property(x => x.ActorName).HasMaxLength(100); e.Property(x => x.Action).HasMaxLength(80);
            e.Property(x => x.EntityType).HasMaxLength(80); e.Property(x => x.EntityId).HasMaxLength(100);
            e.Property(x => x.TraceId).HasMaxLength(100); e.HasIndex(x => x.OccurredAt);
        });
        m.Entity<EnrollmentHistory>().HasOne<Enrollment>().WithMany().HasForeignKey(x => x.EnrollmentId).OnDelete(DeleteBehavior.Restrict);
        m.Entity<EnrollmentApproval>(e => {
            e.Property(x => x.Status).HasMaxLength(20); e.Property(x => x.Comment).HasMaxLength(1000);
            e.HasIndex(x => new { x.CourseId, x.EmployeeId }).IsUnique().HasFilter("[Status] = 'Pending'");
            e.HasOne(x => x.Course).WithMany().OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Employee).WithMany().OnDelete(DeleteBehavior.Restrict);
        });
        m.Entity<LearningPlan>(e => {
            e.Property(x => x.Title).HasMaxLength(160); e.Property(x => x.Department).HasMaxLength(80);
            e.Property(x => x.JobTitle).HasMaxLength(100);
            e.HasMany(x => x.Courses).WithOne().HasForeignKey(x => x.LearningPlanId).OnDelete(DeleteBehavior.Cascade);
        });
        m.Entity<LearningPlanCourse>().HasKey(x => new { x.LearningPlanId, x.CourseId });
        m.Entity<LearningPlanCourse>().HasOne<Course>().WithMany().HasForeignKey(x => x.CourseId).OnDelete(DeleteBehavior.Restrict);
        m.Entity<LearningAssignment>(e => {
            e.Property(x => x.Cycle).HasMaxLength(20);
            e.HasIndex(x => new { x.LearningPlanId, x.CourseId, x.EmployeeId, x.Cycle }).IsUnique();
            e.HasOne(x => x.LearningPlan).WithMany().OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Employee).WithMany().OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Course).WithMany().OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.DueAt);
        });
        m.Entity<Notification>(e => {
            e.Property(x => x.Title).HasMaxLength(200); e.Property(x => x.Body).HasMaxLength(4000);
            e.Property(x => x.Link).HasMaxLength(100); e.Property(x => x.DedupeKey).HasMaxLength(200);
            e.Property(x => x.DeliveryStatus).HasMaxLength(20); e.HasIndex(x => new { x.UserId, x.DedupeKey }).IsUnique();
            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        });
        m.Entity<CourseMaterial>(e => {
            e.Property(x => x.FileName).HasMaxLength(200); e.Property(x => x.ContentType).HasMaxLength(100);
            e.HasOne<Course>().WithMany().HasForeignKey(x => x.CourseId).OnDelete(DeleteBehavior.Restrict);
        });
        m.Entity<QuizQuestion>(e => {
            e.Property(x => x.Prompt).HasMaxLength(1000); e.Property(x => x.OptionsJson).HasMaxLength(6000);
            e.HasOne<Course>().WithMany().HasForeignKey(x => x.CourseId).OnDelete(DeleteBehavior.Restrict);
        });
        m.Entity<QuizAttempt>(e => {
            e.Property(x => x.Score).HasPrecision(5, 2);
            e.HasOne<Enrollment>().WithMany().HasForeignKey(x => x.EnrollmentId).OnDelete(DeleteBehavior.Restrict);
        });
        m.Entity<CourseSurvey>(e => {
            e.Property(x => x.Feedback).HasMaxLength(2000); e.HasIndex(x => new { x.EnrollmentId, x.Attempt }).IsUnique();
            e.HasOne<Enrollment>().WithMany().HasForeignKey(x => x.EnrollmentId).OnDelete(DeleteBehavior.Restrict);
        });
        m.Entity<Certificate>(e => {
            e.Property(x => x.Serial).HasMaxLength(40); e.Property(x => x.EmployeeName).HasMaxLength(100);
            e.Property(x => x.CourseTitle).HasMaxLength(160); e.Property(x => x.Hours).HasPrecision(8, 2);
            e.HasIndex(x => x.Serial).IsUnique(); e.HasIndex(x => new { x.EnrollmentId, x.Attempt }).IsUnique();
            e.HasOne<Enrollment>().WithMany().HasForeignKey(x => x.EnrollmentId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Employee).WithMany().OnDelete(DeleteBehavior.Restrict);
        });
        m.Entity<AccountInvitation>(e => {
            e.Property(x => x.TokenHash).HasMaxLength(64); e.HasIndex(x => x.TokenHash).IsUnique();
            e.Property(x => x.Email).HasMaxLength(200); e.Property(x => x.Role).HasMaxLength(32);
            e.HasOne<Employee>().WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict);
        });
        m.Entity<ReportSchedule>(e => {
            e.Property(x => x.Title).HasMaxLength(160); e.Property(x => x.Department).HasMaxLength(80);
            e.Property(x => x.Frequency).HasMaxLength(20);
            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        });
        m.Entity<ReportDelivery>().Property(x => x.Title).HasMaxLength(160);
        m.Entity<ReportDelivery>().Property(x => x.ScopeFingerprint).HasMaxLength(64);
        m.Entity<SsoTicket>().HasKey(x => x.Hash);
        m.Entity<SsoTicket>().Property(x => x.Hash).HasMaxLength(64);
    }

    private static readonly HashSet<string> AuditedTypes = [nameof(AppUser), nameof(Employee), nameof(Course), nameof(Enrollment),
        nameof(Department), nameof(EnrollmentApproval), nameof(LearningPlan), nameof(LearningAssignment), nameof(Certificate),
        nameof(QuizQuestion), nameof(CourseMaterial), nameof(ReportSchedule), nameof(CourseSurvey),
        nameof(ApprovalPolicy), nameof(ApprovalStage), nameof(ApprovalWorkflow), nameof(ApprovalDelegation), nameof(CourseSeries), nameof(CourseMeeting), nameof(WaitlistEntry), nameof(TrainingBudget), nameof(BudgetCommitment), nameof(ExpenseClaim), nameof(Skill), nameof(JobSkillRequirement), nameof(SkillAssessment), nameof(SkillCourse), nameof(DevelopmentGoal), nameof(CredentialType), nameof(CredentialRequirement), nameof(ExternalCredential), nameof(IntegrationClient), nameof(ProvisioningGroup), nameof(HrSyncSchedule), nameof(LearningActivity), nameof(WebhookSubscription)];
    private static readonly HashSet<string> SecretFields = ["PasswordHash", "TokenVersion", "RowVersion", "ExternalSubject", "Content", "Receipt", "TokenHash", "ProtectedSecret", "OptionsJson", "CorrectOption"];
    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        ChangeTracker.DetectChanges();
        if (ChangeTracker.Entries<AuditEvent>().Any(x => x.State is EntityState.Modified or EntityState.Deleted))
            throw new InvalidOperationException("稽核紀錄不可修改或刪除。");
        var changes = ChangeTracker.Entries().Where(x => AuditedTypes.Contains(x.Metadata.ClrType.Name)
            && x.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .Select(x => (Entry: x, State: x.State, Before: x.State == EntityState.Added ? null : Snapshot(x, true))).ToList();
        if (changes.Count == 0) return await base.SaveChangesAsync(cancellationToken);
        var ownsTransaction = Database.CurrentTransaction is null;
        await using var transaction = ownsTransaction ? await Database.BeginTransactionAsync(cancellationToken) : null;
        var count = await base.SaveChangesAsync(cancellationToken);
        CaptureIntegrationEvents(changes);
        var context = accessor?.HttpContext;
        foreach (var change in changes)
            AuditEvents.Add(new AuditEvent {
                ActorId = int.TryParse(context?.User.FindFirstValue("sub"), out var id) ? id : null,
                ActorName = context?.User.Identity?.Name ?? "system", Action = change.State.ToString(),
                EntityType = change.Entry.Metadata.ClrType.Name,
                EntityId = string.Join(",", change.Entry.Properties.Where(x => x.Metadata.IsPrimaryKey()).Select(x => x.CurrentValue)),
                BeforeJson = change.Before, AfterJson = change.State == EntityState.Deleted ? null : Snapshot(change.Entry, false),
                TraceId = context?.TraceIdentifier ?? "background"
            });
        await base.SaveChangesAsync(cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        return count;
    }
    private static string Snapshot(EntityEntry entry, bool original) => JsonSerializer.Serialize(entry.Properties
        .Where(p => !SecretFields.Contains(p.Metadata.Name)).ToDictionary(p => p.Metadata.Name, p => original ? p.OriginalValue : p.CurrentValue));
}
