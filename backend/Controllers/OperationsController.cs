using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TrainingManagement.Api.Data;
using TrainingManagement.Api.Infrastructure;

namespace TrainingManagement.Api.Controllers;
[ApiController, Route("api/enterprise/operations"), Authorize(Roles = "Admin")]
public sealed class OperationsController(TrainingDbContext db, EnterpriseJobs jobs, ApplicationHealth health) : ControllerBase
{
    [HttpPost("run")]
    public async Task<IActionResult> Run(CancellationToken ct)
    {
        var assigned = await jobs.RunAssignments(null, ct); await jobs.RunRemindersAndReports(ct);
        health.JobSucceeded();
        return Ok(new { assigned, status = "completed" });
    }
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct) => Ok(new { runtime = health.Snapshot(), databaseReady = await db.Database.CanConnectAsync(ct),
        failedNotifications = await db.Notifications.CountAsync(x => x.DeliveryStatus == "Failed", ct),
        pendingApprovals = await db.Approvals.CountAsync(x => x.Status == "Pending", ct),
        unreadNotifications = await db.Notifications.CountAsync(x => x.ReadAt == null, ct),
        overdueAssignments = await db.Assignments.CountAsync(x => x.CompletedAt == null && x.DueAt < DateTime.UtcNow, ct) });
}
