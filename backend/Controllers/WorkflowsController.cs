using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TrainingManagement.Api.Data;
using TrainingManagement.Api.Infrastructure;
using TrainingManagement.Api.Models;
namespace TrainingManagement.Api.Controllers;
[ApiController,Route("api/advanced")]
public sealed class WorkflowsController(TrainingDbContext db,DataScope scope,AdvancedAccess access,MutationGate gate,WorkflowService workflows):ControllerBase
{
    [HttpGet("policies")]
    public async Task<IActionResult> Policies(CancellationToken ct)=>Ok(await db.ApprovalPolicies.AsNoTracking().OrderBy(x=>x.Name).Select(x=>new{x.Id,x.Name,x.MinimumAmount,x.StagesJson,x.IsActive,rowVersion=Convert.ToBase64String(x.RowVersion)}).ToListAsync(ct));
    [HttpPost("policies"),Authorize(Roles=Roles.Administrators)]
    public async Task<IActionResult> Create(PolicyRequest r,CancellationToken ct){await Validate(r,ct);var p=new ApprovalPolicy{Name=r.Name.Trim(),MinimumAmount=r.MinimumAmount,StagesJson=JsonSerializer.Serialize(r.Stages),IsActive=r.IsActive};db.ApprovalPolicies.Add(p);await db.SaveChangesAsync(ct);return StatusCode(201,new{p.Id});}
    [HttpPut("policies/{id:int}"),Authorize(Roles=Roles.Administrators)]
    public async Task<IActionResult> Update(int id,PolicyRequest r,CancellationToken ct){await Validate(r,ct);var p=await db.ApprovalPolicies.FindAsync([id],ct)??throw new ApiException(404,"流程不存在。");if(string.IsNullOrEmpty(r.RowVersion))throw new ApiException(400,"請提供版本。");DataScope.CheckVersion(p.RowVersion,r.RowVersion);p.Name=r.Name.Trim();p.MinimumAmount=r.MinimumAmount;p.StagesJson=JsonSerializer.Serialize(r.Stages);p.IsActive=r.IsActive;await db.SaveChangesAsync(ct);return NoContent();}
    private async Task Validate(PolicyRequest r,CancellationToken ct){if(string.IsNullOrWhiteSpace(r.Name)||r.Stages.Any(x=>string.IsNullOrWhiteSpace(x.Name)||x.Kind=="User"&&!x.UserId.HasValue))throw new ApiException(400,"流程名稱、關卡名稱與指定簽核人必填。");foreach(var id in r.Stages.Where(x=>x.Kind=="User").Select(x=>x.UserId))if(!await db.AppUsers.AnyAsync(x=>x.Id==id&&x.IsActive&&x.IsApproved,ct))throw new ApiException(400,"指定簽核人未啟用。");}
    [HttpGet("workflows")]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var people=await access.People(ct);var now=DateTime.UtcNow;
        var delegated=await db.Delegations.Where(x=>x.DelegateUserId==scope.UserId&&x.IsActive&&x.StartsAt<=now&&x.EndsAt>now).Select(x=>x.PrincipalUserId).ToArrayAsync(ct);
        var flows=await db.Workflows.AsNoTracking().Include(x=>x.Stages).Include(x=>x.Employee).Where(x=>people.Any(p=>p.Id==x.EmployeeId)
            ||x.Stages.Any(s=>s.Status=="Pending"&&(s.ApproverUserId==scope.UserId||delegated.Contains(s.ApproverUserId??0)||s.ApproverKind=="HR"&&User.IsInRole("HR"))))
            .OrderByDescending(x=>x.Id).Take(200).ToListAsync(ct);
        var result=new List<object>();foreach(var f in flows){var stage=f.Stages.FirstOrDefault(x=>x.Status=="Pending");var can=stage is not null&&(await workflows.CanAct(f,stage,scope.UserId,ct)).Allowed;
            result.Add(new{f.Id,f.Kind,f.Title,f.Amount,employeeName=f.Employee.Name,f.Status,f.CreatedAt,f.CompletedAt,canAct=can,rowVersion=Convert.ToBase64String(f.RowVersion),stages=f.Stages.OrderBy(x=>x.Position).Select(s=>new{s.Id,s.Name,s.Position,s.ApproverKind,s.ApproverUserId,s.Status,s.DueAt,s.ActedAt,s.ActorUserId,s.RepresentedUserId,s.Comment})});}return Ok(result);
    }
    [HttpPost("workflows/{id:int}/decision")]
    public async Task<IActionResult> Decide(int id,ApprovalDecision r,CancellationToken ct){using var write=await gate.EnterAsync(ct);await using var tx=await db.Database.BeginTransactionAsync(ct);var f=await workflows.Decide(id,r,ct);await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);return Ok(new{f.Status});}
    [HttpGet("delegations")]
    public async Task<IActionResult> Delegations(CancellationToken ct)=>Ok(await db.Delegations.Where(x=>scope.IsGlobal||x.PrincipalUserId==scope.UserId||x.DelegateUserId==scope.UserId).OrderByDescending(x=>x.Id).Select(x=>new{x.Id,x.PrincipalUserId,x.DelegateUserId,x.StartsAt,x.EndsAt,x.Reason,x.IsActive,rowVersion=Convert.ToBase64String(x.RowVersion)}).ToListAsync(ct));
    [HttpPost("delegations")]
    public async Task<IActionResult> Delegate(DelegationRequest r,CancellationToken ct)
    {
        if(!scope.User.IsInRole("Admin")&&r.PrincipalUserId!=scope.UserId)throw new ApiException(403,"僅能設定自己的代理人。");
        if(r.PrincipalUserId==r.DelegateUserId||r.StartsAt==default||r.EndsAt<=r.StartsAt||r.EndsAt-r.StartsAt>TimeSpan.FromDays(90)||r.EndsAt>DateTimeOffset.UtcNow.AddDays(90))throw new ApiException(400,"代理期限不可超過 90 天，代理人不可為本人。");
        if(await db.AppUsers.CountAsync(x=>(x.Id==r.PrincipalUserId||x.Id==r.DelegateUserId)&&x.IsActive&&x.IsApproved,ct)!=2)throw new ApiException(400,"代理雙方須為已核准的啟用帳號。");
        var d=new ApprovalDelegation{PrincipalUserId=r.PrincipalUserId,DelegateUserId=r.DelegateUserId,StartsAt=r.StartsAt.UtcDateTime,EndsAt=r.EndsAt.UtcDateTime,Reason=r.Reason.Trim()};db.Delegations.Add(d);await db.SaveChangesAsync(ct);return StatusCode(201,new{d.Id});
    }
    [HttpPost("delegations/{id:int}/revoke")]
    public async Task<IActionResult> Revoke(int id,CancellationToken ct){var d=await db.Delegations.SingleOrDefaultAsync(x=>x.Id==id&&(User.IsInRole("Admin")||x.PrincipalUserId==scope.UserId),ct)??throw new ApiException(404,"找不到可管理的代理設定。");d.IsActive=false;await db.SaveChangesAsync(ct);return NoContent();}
    [HttpGet("people"),Authorize(Roles=Roles.Administrators)]
    public async Task<IActionResult> People([FromQuery]string? search,[FromQuery]int page=1,CancellationToken ct=default){var q=db.AppUsers.Where(x=>x.IsActive&&x.IsApproved);if(!string.IsNullOrWhiteSpace(search))q=q.Where(x=>x.DisplayName.Contains(search)||x.Username.Contains(search));return Ok(new{total=await q.CountAsync(ct),items=await q.OrderBy(x=>x.Id).Skip((Math.Clamp(page,1,100000)-1)*30).Take(30).Select(x=>new{x.Id,x.DisplayName,x.Username,x.Role,x.EmployeeId}).ToListAsync(ct)});}
}
