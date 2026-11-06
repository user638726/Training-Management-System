using Microsoft.EntityFrameworkCore;
using TrainingManagement.Api.Data;
namespace TrainingManagement.Api.Infrastructure;
public sealed class AdvancedJobs(TrainingDbContext db,MutationGate gate,WaitlistService waitlist,TrainingService training,HrSyncService hr,WebhookService webhooks)
{
    public async Task RunLocal(CancellationToken ct)
    {
        using var write=await gate.EnterAsync(ct);await using var tx=await db.Database.BeginTransactionAsync(ct);await AdvancedAccess.Lock(db,"advanced-local-jobs",ct);
        var courseIds=await db.Waitlist.Where(x=>x.Status=="Waiting"||x.Status=="Offered").Select(x=>x.CourseId).Distinct().ToListAsync(ct);
        foreach(var id in courseIds){await CourseWriteLock.AcquireAsync(db,id,ct);await waitlist.Promote(id,ct);}
        var now=DateTime.UtcNow;var day=now.AddHours(8).ToString("yyyyMMdd");
        foreach(var stage in await db.ApprovalStages.Include(x=>x.Workflow).Where(x=>x.Status=="Pending"&&x.Workflow.Status=="Pending"&&x.DueAt<now.AddHours(24)).ToListAsync(ct)){
            var recipients=stage.ApproverKind=="HR"?await db.AppUsers.Where(x=>x.Role=="HR"&&x.IsActive&&x.IsApproved).Select(x=>x.Id).ToListAsync(ct):stage.ApproverUserId.HasValue?new List<int>{stage.ApproverUserId.Value}:new List<int>();
            recipients.AddRange(await db.Delegations.Where(x=>recipients.Contains(x.PrincipalUserId)&&x.IsActive&&x.StartsAt<=now&&x.EndsAt>now).Select(x=>x.DelegateUserId).ToListAsync(ct));
            if(stage.DueAt<now)recipients.AddRange(await db.AppUsers.Where(x=>(x.Role=="HR"||x.Role=="Admin")&&x.IsActive&&x.IsApproved&&x.EmployeeId!=stage.Workflow.EmployeeId).Select(x=>x.Id).ToListAsync(ct));
            foreach(var id in recipients.Distinct())await training.Notify(id,stage.DueAt<now?"簽核關卡逾期":"簽核關卡即將到期",$"「{stage.Workflow.Title}」的「{stage.Name}」需要處理；不會自動核准。",$"flow-remind:{stage.Id}:{day}","/workflows",ct);
        }
        var requirements=await db.CredentialRequirements.ToListAsync(ct);
        foreach(var c in await db.ExternalCredentials.Include(x=>x.Employee).Include(x=>x.CredentialType).Where(x=>x.Status=="Approved"&&x.Employee.IsActive&&x.ExpiresAt!=null&&x.ExpiresAt>=now.AddDays(-30)&&x.ExpiresAt<=now.AddDays(365)).ToListAsync(ct)){
            var days=requirements.FirstOrDefault(x=>x.JobTitle==c.Employee.JobTitle&&x.CredentialTypeId==c.CredentialTypeId)?.RenewalNoticeDays??60;
            if(c.ExpiresAt>now.AddDays(days))continue;
            await training.NotifyEmployee(c.EmployeeId,c.ExpiresAt<now?"外部證照已到期":"外部證照即將到期",$"「{c.CredentialType.Name}」到期日 {c.ExpiresAt:yyyy/MM/dd}，請申請續證。",$"credential-expiry:{c.Id}:{day}",ct);
        }
        foreach(var goal in await db.Goals.Where(x=>x.Status=="Active"&&x.DueAt<now.AddDays(3)&&x.Employee.IsActive).ToListAsync(ct))await training.NotifyEmployee(goal.EmployeeId,"成長計畫期限提醒",$"「{goal.Title}」期限為 {goal.DueAt:yyyy/MM/dd}。",$"goal:{goal.Id}:{day}",ct);
        await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);
    }
    public async Task RunExternal(CancellationToken ct)
    {
        var schedules=await db.HrSyncSchedules.Where(x=>x.IsActive&&x.NextRunAt<=DateTime.UtcNow).Select(x=>x.Id).ToListAsync(ct);
        foreach(var id in schedules)await hr.Run(id,ct);
        await webhooks.Deliver(ct);
    }
}
