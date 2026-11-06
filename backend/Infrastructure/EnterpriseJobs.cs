using System.Net;
using System.Net.Mail;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using TrainingManagement.Api.Data;
using TrainingManagement.Api.Models;

namespace TrainingManagement.Api.Infrastructure;

public sealed class EnterpriseJobs(TrainingDbContext db, TrainingService training, MutationGate gate, DataScope scope,
    ReportingService reports, IConfiguration config, ILogger<EnterpriseJobs> logger)
{
    public async Task<int> RunAssignments(int? planId, CancellationToken ct)
    {
        using var write = await gate.EnterAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await Lock(ct);
        var plans = await db.LearningPlans.Include(x => x.Courses).Where(x => x.IsActive && (planId == null || x.Id == planId)).ToListAsync(ct);
        var count = 0; var now = DateTime.UtcNow;
        foreach (var p in plans) {
            var months = (now.Year - p.CreatedAt.Year) * 12 + now.Month - p.CreatedAt.Month;
            var cycleIndex = p.RecurrenceMonths > 0 ? months / p.RecurrenceMonths : 0;
            var cycle = p.RecurrenceMonths > 0 ? p.CreatedAt.AddMonths(cycleIndex * p.RecurrenceMonths).ToString("yyyy-MM") : "once";
            var hiredSince = p.NewHireDays.HasValue ? now.Date.AddDays(-p.NewHireDays.Value) : DateTime.MinValue;
            var employees = await db.Employees.Where(x => x.IsActive && (p.Department == null || x.Department == p.Department)
                && (p.JobTitle == null || x.JobTitle == p.JobTitle) && x.HiredAt >= hiredSince && x.HiredAt <= now.Date).ToListAsync(ct);
            var existing = await db.Assignments.Where(x => x.LearningPlanId == p.Id && x.Cycle == cycle)
                .Select(x => new { x.EmployeeId, x.CourseId }).ToListAsync(ct);
            var keys = existing.Select(x => (x.EmployeeId, x.CourseId)).ToHashSet();
            foreach (var e in employees) foreach (var c in p.Courses) {
                if (!keys.Add((e.Id, c.CourseId))) continue;
                db.Assignments.Add(new LearningAssignment { LearningPlanId = p.Id, EmployeeId = e.Id, CourseId = c.CourseId,
                    Cycle = cycle, AssignedAt = now, DueAt = now.AddDays(p.DueDays) }); count++;
                await training.NotifyEmployee(e.Id, "新的必修任務", $"「{p.Title}」已指派課程，請於 {now.AddDays(p.DueDays).AddHours(8):yyyy/MM/dd} 前完成。",
                    $"assigned:{p.Id}:{cycle}:{e.Id}:{c.CourseId}", ct);
            }
        }
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return count;
    }
    public async Task RunRemindersAndReports(CancellationToken ct)
    {
        using var write = await gate.EnterAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct); await Lock(ct);
        var now = DateTime.UtcNow; var tomorrow = now.AddDays(1); var day = now.AddHours(8).ToString("yyyyMMdd");
        var due = await db.Assignments.Include(x => x.Course).Where(x => x.CompletedAt == null && x.DueAt < tomorrow && x.Employee.IsActive && x.LearningPlan.IsActive).ToListAsync(ct);
        foreach (var a in due) {
            await training.NotifyEmployee(a.EmployeeId, a.DueAt < now ? "必修任務已逾期" : "必修任務即將到期",
                $"「{a.Course.Title}」期限為 {a.DueAt.AddHours(8):yyyy/MM/dd}。", $"due:{a.Id}:{day}", ct);
            if (a.DueAt < now) {
                var department = await db.Employees.Where(x => x.Id == a.EmployeeId).Select(x => x.Department).SingleAsync(ct);
                foreach (var manager in await db.Departments.Where(x => x.Name == department && x.ManagerUserId != null).Select(x => x.ManagerUserId).ToListAsync(ct))
                    await training.Notify(manager, "部門必修逾期", $"部門「{department}」有未完成的必修課程，請查看主管報表。", $"manager-due:{a.Id}:{day}", "/management-reports", ct);
            }
        }
        var upcoming = await db.Enrollments.Include(x => x.Course).Where(x => x.Status != EnrollmentStatus.Cancelled && x.Status != EnrollmentStatus.Completed
            && x.Course.Status == CourseStatus.Published && x.Course.StartDate >= now && x.Course.StartDate <= tomorrow).ToListAsync(ct);
        foreach (var e in upcoming) await training.NotifyEmployee(e.EmployeeId, "開課提醒", $"「{e.Course.Title}」將於 {e.Course.StartDate.AddHours(8):MM/dd HH:mm} 開始。", $"starts:{e.Id}:{day}", ct);
        foreach (var c in await db.Certificates.Where(x => x.RevokedAt == null && x.ExpiresAt >= now && x.ExpiresAt <= now.AddDays(30)).ToListAsync(ct))
            await training.NotifyEmployee(c.EmployeeId, "證書即將到期", $"「{c.CourseTitle}」證書將於 {c.ExpiresAt:yyyy/MM/dd} 到期。", $"expires:{c.Id}", ct);
        foreach (var schedule in await db.ReportSchedules.Where(x => x.IsActive && x.NextRunAt <= now).ToListAsync(ct)) {
            var u = await db.AppUsers.SingleOrDefaultAsync(x => x.Id == schedule.UserId && x.IsActive && x.IsApproved, ct);
            if (u is null || !new[] { "Admin", "HR", "Manager" }.Contains(u.Role)) { schedule.IsActive = false; continue; }
            scope.PrincipalOverride = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", u.Id.ToString()), new Claim("role", u.Role), new Claim("name", u.Username)], "job", "name", "role"));
            try {
                var delivery = new ReportDelivery { UserId = u.Id, Title = schedule.Title, PermissionVersion = u.TokenVersion,
                    ScopeFingerprint = await reports.PermissionFingerprint(ct),
                    Content = await reports.Csv(now.AddDays(-30), now, schedule.Department, ct) };
                db.ReportDeliveries.Add(delivery);
                await training.Notify(u.Id, "定期訓練報表已產生", $"「{schedule.Title}」已完成，可從主管報表下載。",
                    $"report:{schedule.Id}:{schedule.NextRunAt:O}", "/management-reports", ct);
                schedule.NextRunAt = schedule.Frequency == "Monthly" ? now.AddMonths(1) : now.AddDays(7);
            } finally { scope.PrincipalOverride = null; }
        }
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
    }
    public async Task DeliverMail(CancellationToken ct)
    {
        if (!config.GetValue("Mail:Enabled", false)) return;
        var host = config["Mail:Host"]; var from = config["Mail:From"];
        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(from)) return;
        using var write = await gate.EnterAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct); await Lock(ct);
        var messages = await db.Notifications.Where(x => (x.DeliveryStatus == "InApp" || x.DeliveryStatus == "Retry")
            && x.DeliveryAttempts < 3 && (x.NextDeliveryAt == null || x.NextDeliveryAt <= DateTime.UtcNow)).OrderBy(x => x.Id).Take(20).ToListAsync(ct);
        foreach (var n in messages) {
            var address = await db.AppUsers.Where(x => x.Id == n.UserId && x.IsActive && x.IsApproved && x.Employee != null && x.Employee.IsActive)
                .Select(x => x.Employee!.Email).SingleOrDefaultAsync(ct);
            if (address is null) { n.DeliveryStatus = "NoAddress"; continue; }
            n.DeliveryAttempts++;
            try {
                using var smtp = new SmtpClient(host, config.GetValue("Mail:Port", 587)) { EnableSsl = true, Timeout = 10000 };
                if (!string.IsNullOrWhiteSpace(config["Mail:Username"])) smtp.Credentials = new NetworkCredential(config["Mail:Username"], config["Mail:Password"]);
                using var message = new MailMessage(from, address, n.Title, n.Body) { IsBodyHtml = false };
                await smtp.SendMailAsync(message, ct); n.DeliveryStatus = "Sent";
            } catch (Exception ex) when (ex is SmtpException or InvalidOperationException or FormatException) {
                n.DeliveryStatus = n.DeliveryAttempts >= 3 ? "Failed" : "Retry";
                n.NextDeliveryAt = DateTime.UtcNow.AddMinutes(n.DeliveryAttempts * 5);
                logger.LogWarning("Mail delivery failed for notification {NotificationId}; attempt {Attempt}.", n.Id, n.DeliveryAttempts);
            }
        }
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
    }
    private Task Lock(CancellationToken ct) => db.Database.ExecuteSqlRawAsync("DECLARE @r int; EXEC @r = sys.sp_getapplock @Resource=N'training-enterprise-jobs', @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=10000; IF @r < 0 THROW 51001, 'Enterprise job lock timeout', 1;", ct);
}
public sealed class EnterpriseWorker(IServiceScopeFactory scopeFactory, ILogger<EnterpriseWorker> logger, IConfiguration config, ApplicationHealth health) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!config.GetValue("Enterprise:WorkerEnabled", true)) return;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(60));
        while (await timer.WaitForNextTickAsync(stoppingToken)) {
            try {
                using var scope = scopeFactory.CreateScope(); var jobs = scope.ServiceProvider.GetRequiredService<EnterpriseJobs>();
                await jobs.RunAssignments(null, stoppingToken); await jobs.RunRemindersAndReports(stoppingToken);
                var advanced=scope.ServiceProvider.GetRequiredService<AdvancedJobs>(); await advanced.RunLocal(stoppingToken); await advanced.RunExternal(stoppingToken);
                await jobs.DeliverMail(stoppingToken);
                health.JobSucceeded();
            } catch (Exception ex) when (!stoppingToken.IsCancellationRequested) { logger.LogError(ex, "Enterprise scheduled work failed; retrying next cycle."); }
        }
    }
}
