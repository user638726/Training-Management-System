using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TrainingManagement.Api.Data;
using TrainingManagement.Api.Infrastructure;
using TrainingManagement.Api.Models;

namespace TrainingManagement.Api.Controllers;

[ApiController, Route("api/enterprise")]
public sealed class ReportingController(TrainingDbContext db, DataScope scope, ReportingService reports) : ControllerBase
{
    [HttpGet("reports")]
    public async Task<IActionResult> Report([FromQuery] DateTime? from, [FromQuery] DateTime? to, [FromQuery] string? department, CancellationToken ct)
        => Ok(await reports.Management(from, to, department, ct));
    [HttpGet("reports/export")]
    public async Task<IActionResult> Export([FromQuery] DateTime? from, [FromQuery] DateTime? to, [FromQuery] string? department, [FromQuery] string? status, CancellationToken ct)
    {
        var csv = await reports.Csv(from, to, department, ct, status);
        db.AuditEvents.Add(new AuditEvent { ActorId = scope.UserId, ActorName = User.Identity?.Name ?? "", Action = "Export",
            EntityType = "TrainingReport", EntityId = department ?? "scoped", TraceId = HttpContext.TraceIdentifier });
        await db.SaveChangesAsync(ct); return File(csv, "text/csv; charset=utf-8", $"training-{DateTime.UtcNow:yyyyMMdd}.csv");
    }
    [HttpGet("report-schedules"), Authorize(Roles = Roles.Reviewers)]
    public async Task<IActionResult> Schedules(CancellationToken ct) => Ok(await db.ReportSchedules.Where(x => x.UserId == scope.UserId).ToListAsync(ct));
    [HttpPost("report-schedules"), Authorize(Roles = Roles.Reviewers)]
    public async Task<IActionResult> Schedule(ScheduleRequest r, CancellationToken ct)
    {
        if (await db.ReportSchedules.CountAsync(x => x.UserId == scope.UserId && x.IsActive, ct) >= 10) throw new ApiException(409, "最多可啟用十個報表排程。");
        if (!string.IsNullOrWhiteSpace(r.Department) && !await (await scope.Employees(ct)).AnyAsync(x => x.Department == r.Department, ct))
            throw new ApiException(403, "您無權訂閱此部門報表。");
        var s = new ReportSchedule { UserId = scope.UserId, Title = r.Title.Trim(), Department = r.Department, Frequency = r.Frequency };
        db.ReportSchedules.Add(s); await db.SaveChangesAsync(ct); return StatusCode(201, new { s.Id });
    }
    [HttpDelete("report-schedules/{id:int}"), Authorize(Roles = Roles.Reviewers)]
    public async Task<IActionResult> Stop(int id, CancellationToken ct)
    {
        var s = await db.ReportSchedules.SingleOrDefaultAsync(x => x.Id == id && x.UserId == scope.UserId, ct) ?? throw new ApiException(404, "找不到排程。");
        s.IsActive = false; await db.SaveChangesAsync(ct); return NoContent();
    }
    [HttpGet("report-deliveries"), Authorize(Roles = Roles.Reviewers)]
    public async Task<IActionResult> Deliveries(CancellationToken ct) => Ok(await db.ReportDeliveries.Where(x => x.UserId == scope.UserId)
        .OrderByDescending(x => x.Id).Take(50).Select(x => new { x.Id, x.Title, x.CreatedAt }).ToListAsync(ct));
    [HttpGet("report-deliveries/{id:long}/download"), Authorize(Roles = Roles.Reviewers)]
    public async Task<IActionResult> Download(long id, CancellationToken ct)
    {
        var delivery = await db.ReportDeliveries.SingleOrDefaultAsync(x => x.Id == id && x.UserId == scope.UserId, ct) ?? throw new ApiException(404, "找不到報表。");
        // Historical delivery content is only available while the subscriber retains the permission version used to create it.
        if (delivery.ScopeFingerprint != await reports.PermissionFingerprint(ct)) throw new ApiException(403, "資料範圍已變更，請重新產生目前權限範圍的報表。");
        return File(delivery.Content, "text/csv; charset=utf-8", $"report-{id}.csv");
    }
}
