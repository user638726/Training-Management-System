using System.Security.Cryptography;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TrainingManagement.Api.Data;
using TrainingManagement.Api.Infrastructure;
using TrainingManagement.Api.Models;
namespace TrainingManagement.Api.Controllers;
[ApiController,Route("api/advanced"),Authorize(Roles="Admin")]
public sealed class IntegrationManagementController(TrainingDbContext db,WebhookService webhooks,HrSyncService hr,IConfiguration configuration):ControllerBase
{
    private static string Token()=>"tm_"+Microsoft.IdentityModel.Tokens.Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));
    [HttpGet("clients")]
    public async Task<IActionResult> Clients(CancellationToken ct)=>Ok(await db.IntegrationClients.OrderByDescending(x=>x.Id).Select(x=>new{x.Id,x.Name,x.Scopes,x.IsActive,x.CreatedAt,x.ExpiresAt,rowVersion=Convert.ToBase64String(x.RowVersion)}).ToListAsync(ct));
    [HttpPost("clients")]
    public async Task<IActionResult> Client(ClientRequest r,CancellationToken ct){if(r.Scopes.Any(s=>!IntegrationAuthentication.AllowedScopes.Contains(s)))throw new ApiException(400,"服務帳號範圍不正確。");var token=Token();var c=new IntegrationClient{Name=r.Name.Trim(),TokenHash=IntegrationAuthentication.Hash(token),Scopes=string.Join(' ',r.Scopes.Distinct()),ExpiresAt=DateTime.UtcNow.AddDays(r.ValidDays)};db.IntegrationClients.Add(c);await db.SaveChangesAsync(ct);return StatusCode(201,new{c.Id,token,c.ExpiresAt});}
    [HttpPost("clients/{id:int}/revoke")]
    public async Task<IActionResult> Revoke(int id,CancellationToken ct){var c=await db.IntegrationClients.FindAsync([id],ct)??throw new ApiException(404,"找不到服務帳號。");c.IsActive=false;await db.SaveChangesAsync(ct);return NoContent();}
    [HttpPost("clients/{id:int}/rotate")]
    public async Task<IActionResult> Rotate(int id,ApprovalDecision r,CancellationToken ct){var c=await db.IntegrationClients.FindAsync([id],ct)??throw new ApiException(404,"找不到服務帳號。");DataScope.CheckVersion(c.RowVersion,r.RowVersion);var token=Token();c.TokenHash=IntegrationAuthentication.Hash(token);c.ExpiresAt=DateTime.UtcNow.AddDays(90);c.IsActive=true;await db.SaveChangesAsync(ct);return Ok(new{token,c.ExpiresAt});}
    [HttpGet("webhooks")]
    public async Task<IActionResult> Subscriptions(CancellationToken ct)=>Ok(await db.WebhookSubscriptions.OrderByDescending(x=>x.Id).Select(x=>new{x.Id,x.Name,x.Endpoint,x.EventTypes,x.IsActive,x.CreatedAt,rowVersion=Convert.ToBase64String(x.RowVersion)}).ToListAsync(ct));
    [HttpPost("webhooks")]
    public async Task<IActionResult> Subscribe(WebhookRequest r,CancellationToken ct){if(r.Events.Any(e=>!WebhookService.EventTypes.Contains(e)))throw new ApiException(400,"事件類型不正確。");await webhooks.ValidateEndpoint(r.Endpoint,ct);var secret=Convert.ToHexString(RandomNumberGenerator.GetBytes(32));var s=new WebhookSubscription{Name=r.Name.Trim(),Endpoint=r.Endpoint,EventTypes=string.Join(' ',r.Events.Distinct()),ProtectedSecret=webhooks.Protect(secret)};db.WebhookSubscriptions.Add(s);await db.SaveChangesAsync(ct);return StatusCode(201,new{s.Id,secret});}
    [HttpPost("webhooks/{id:int}/disable")]
    public async Task<IActionResult> Disable(int id,CancellationToken ct){var s=await db.WebhookSubscriptions.FindAsync([id],ct)??throw new ApiException(404,"找不到訂閱。");s.IsActive=false;await db.SaveChangesAsync(ct);return NoContent();}
    [HttpGet("webhook-deliveries")]
    public async Task<IActionResult> Deliveries(CancellationToken ct)=>Ok(await db.WebhookDeliveries.OrderByDescending(x=>x.Id).Take(500).Select(x=>new{x.Id,x.EventId,eventType=x.Event.Type,x.SubscriptionId,subscriptionName=x.Subscription.Name,x.Status,x.Attempts,x.NextAttemptAt,x.LastHttpStatus,x.LastError}).ToListAsync(ct));
    [HttpGet("webhook-deliveries/{id:long}/attempts")]
    public async Task<IActionResult> Attempts(long id,CancellationToken ct)=>Ok(await db.WebhookAttempts.Where(x=>x.DeliveryId==id).OrderByDescending(x=>x.Id).ToListAsync(ct));
    [HttpPost("webhook-deliveries/{id:long}/retry")]
    public async Task<IActionResult> Retry(long id,CancellationToken ct){var d=await db.WebhookDeliveries.FindAsync([id],ct)??throw new ApiException(404,"找不到寄送工作。");if(d.Status=="Delivered"||d.LeaseUntil>DateTime.UtcNow)throw new ApiException(409,"已成功或執行中的事件不可重送。");d.Status="Retry";d.Attempts=0;d.NextAttemptAt=DateTime.UtcNow;await db.SaveChangesAsync(ct);return NoContent();}
    [HttpPost("webhooks/process")]
    public async Task<IActionResult> Process(CancellationToken ct){await webhooks.Deliver(ct);return Ok(new{status="processed"});}
    [HttpGet("provisioning-logs")]
    public async Task<IActionResult> Logs(CancellationToken ct)=>Ok(await db.ProvisioningLogs.OrderByDescending(x=>x.Id).Take(500).ToListAsync(ct));
    [HttpGet("provisioning-groups")]
    public async Task<IActionResult> Groups(CancellationToken ct)=>Ok(await db.ProvisioningGroups.OrderBy(x=>x.DisplayName).ToListAsync(ct));
    public sealed class GroupDepartmentRequest{public int DepartmentId{get;set;}}
    [HttpPost("provisioning-groups/{id:int}/department")]
    public async Task<IActionResult> Department(int id,GroupDepartmentRequest r,CancellationToken ct){var group=await db.ProvisioningGroups.FindAsync([id],ct)??throw new ApiException(404,"找不到群組。");var d=await db.Departments.FindAsync([r.DepartmentId],ct)??throw new ApiException(400,"部門不存在。");var members=System.Text.Json.JsonSerializer.Deserialize<int[]>(group.MembersJson)??[];group.Department=d.Name;foreach(var user in await db.AppUsers.Include(x=>x.Employee).Where(x=>members.Contains(x.Id)&&x.ProvisioningClientId==group.ClientId&&x.Role=="Employee").ToListAsync(ct)){user.Employee!.Department=d.Name;user.TokenVersion++;}await db.SaveChangesAsync(ct);return NoContent();}
    [HttpPost("provisioning-groups/{id:int}/unlink")]
    public async Task<IActionResult> Unlink(int id,CancellationToken ct){var group=await db.ProvisioningGroups.FindAsync([id],ct)??throw new ApiException(404,"找不到群組。");group.Department=null;await db.SaveChangesAsync(ct);return NoContent();}
    [HttpGet("hr-schedules")]
    public async Task<IActionResult> Schedules(CancellationToken ct)=>Ok(await db.HrSyncSchedules.OrderBy(x=>x.Id).ToListAsync(ct));
    [HttpPost("hr-schedules")]
    public async Task<IActionResult> Schedule(HrScheduleRequest r,CancellationToken ct){if(string.IsNullOrWhiteSpace(configuration["Hr:EmployeesEndpoint"]))throw new ApiException(409,"請先設定 Hr:EmployeesEndpoint。");if(await db.HrSyncSchedules.AnyAsync(x=>x.IsActive,ct))throw new ApiException(409,"同一人事來源只允許一個啟用排程。");var s=new HrSyncSchedule{Name=r.Name.Trim(),IntervalMinutes=r.IntervalMinutes,AllowDeactivation=r.AllowDeactivation,IsActive=r.IsActive};db.HrSyncSchedules.Add(s);await db.SaveChangesAsync(ct);return StatusCode(201,new{s.Id});}
    [HttpPost("hr-schedules/{id:int}/run")]
    public async Task<IActionResult> Run(int id,CancellationToken ct){await hr.Run(id,ct);return Ok(new{status="finished"});}
    [HttpPost("hr-schedules/{id:int}/disable")]
    public async Task<IActionResult> Stop(int id,CancellationToken ct){var s=await db.HrSyncSchedules.FindAsync([id],ct)??throw new ApiException(404,"找不到排程。");s.IsActive=false;await db.SaveChangesAsync(ct);return NoContent();}
}
