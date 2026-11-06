using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TrainingManagement.Api.Data;
using TrainingManagement.Api.Models;
namespace TrainingManagement.Api.Infrastructure;
public sealed class WorkflowService(TrainingDbContext db,DataScope scope,TrainingService training,BudgetService budgets)
{
    public async Task<ApprovalWorkflow?> Initialize(string kind,int relatedId,int employeeId,int? policyId,decimal amount,string title,int? budgetOwner,CancellationToken ct)
    {
        if(policyId is null) return null;
        var policy=await db.ApprovalPolicies.SingleOrDefaultAsync(x=>x.Id==policyId && x.IsActive,ct) ?? throw new ApiException(409,"簽核流程已停用。");
        if(amount<policy.MinimumAmount) return null;
        var definitions=JsonSerializer.Deserialize<StageDefinition[]>(policy.StagesJson)!;
        var employee=await db.Employees.FindAsync([employeeId],ct) ?? throw new ApiException(409,"找不到員工。");
        var workflow=new ApprovalWorkflow{Kind=kind,RelatedId=relatedId,EmployeeId=employeeId,PolicyId=policy.Id,Amount=amount,Title=title};
        var manager=await ManagerFor(employee,ct); var position=0;
        foreach(var d in definitions){
            var userId=d.Kind switch{"Manager"=>manager,"BudgetOwner"=>budgetOwner,"User"=>d.UserId,_=>null};
            if(d.Kind!="HR" && (!userId.HasValue || !await db.AppUsers.AnyAsync(x=>x.Id==userId && x.IsActive && x.IsApproved && x.EmployeeId!=employeeId,ct)))
                throw new ApiException(409,$"關卡「{d.Name}」沒有可用且非申請者本人的簽核人。");
            workflow.Stages.Add(new ApprovalStage{Position=++position,Name=d.Name,ApproverKind=d.Kind,ApproverUserId=userId,
                DueHours=d.DueHours,Status=position==1?"Pending":"Waiting",DueAt=position==1?DateTime.UtcNow.AddHours(d.DueHours):null});
        }
        db.Workflows.Add(workflow); await db.SaveChangesAsync(ct); await Notify(workflow,ct); return workflow;
    }
    private async Task<int?> ManagerFor(Employee employee,CancellationToken ct)
    {
        var departments=await db.Departments.AsNoTracking().ToListAsync(ct); var current=departments.FirstOrDefault(x=>x.Name==employee.Department);
        var seen=new HashSet<int>();
        while(current is not null && seen.Add(current.Id)){
            if(current.ManagerUserId.HasValue && await db.AppUsers.AnyAsync(x=>x.Id==current.ManagerUserId && x.Role=="Manager" && x.IsActive && x.IsApproved && x.EmployeeId!=employee.Id,ct)) return current.ManagerUserId;
            current=departments.FirstOrDefault(x=>x.Id==current.ParentId);
        }
        return null;
    }
    public async Task<(bool Allowed,int? Represented)> CanAct(ApprovalWorkflow workflow,ApprovalStage stage,int userId,CancellationToken ct)
    {
        var actor=await db.AppUsers.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==userId && x.IsActive && x.IsApproved,ct);
        if(actor is null || actor.EmployeeId==workflow.EmployeeId || workflow.Status!="Pending" || stage.Status!="Pending") return(false,null);
        if(actor.Role=="Admin") return(true,null);
        if(stage.ApproverKind=="Manager"&&await ManagerFor(workflow.Employee,ct)!=stage.ApproverUserId)return(false,null);
        if(stage.ApproverKind=="BudgetOwner"){
            var owner=workflow.Kind=="Expense"?await db.Expenses.Where(x=>x.Id==workflow.RelatedId).Select(x=>(int?)x.Budget.OwnerUserId).SingleOrDefaultAsync(ct)
                :await db.Approvals.Where(x=>x.Id==workflow.RelatedId).Select(x=>db.Budgets.Where(b=>b.Id==x.Course.BudgetId).Select(b=>(int?)b.OwnerUserId).SingleOrDefault()).SingleOrDefaultAsync(ct);
            if(owner!=stage.ApproverUserId)return(false,null);
        }
        int[] principals;
        if(stage.ApproverKind=="HR") principals=await db.AppUsers.Where(x=>x.Role=="HR" && x.IsActive && x.IsApproved).Select(x=>x.Id).ToArrayAsync(ct);
        else principals=await db.AppUsers.Where(x=>x.Id==stage.ApproverUserId && x.IsActive && x.IsApproved && (stage.ApproverKind!="Manager" || x.Role=="Manager")).Select(x=>x.Id).ToArrayAsync(ct);
        if(principals.Contains(userId)) return(true,null);
        var now=DateTime.UtcNow;
        var principal=await db.Delegations.Where(x=>principals.Contains(x.PrincipalUserId) && x.DelegateUserId==userId && x.IsActive && x.StartsAt<=now && x.EndsAt>now)
            .Select(x=>(int?)x.PrincipalUserId).FirstOrDefaultAsync(ct);
        return(principal.HasValue,principal);
    }
    public async Task<ApprovalWorkflow> Decide(int id,ApprovalDecision request,CancellationToken ct)
    {
        await AdvancedAccess.Lock(db,$"workflow:{id}",ct);
        var workflow=await db.Workflows.Include(x=>x.Stages).Include(x=>x.Employee).SingleOrDefaultAsync(x=>x.Id==id,ct) ?? throw new ApiException(404,"找不到簽核流程。");
        DataScope.CheckVersion(workflow.RowVersion,request.RowVersion);
        var stage=workflow.Stages.SingleOrDefault(x=>x.Status=="Pending") ?? throw new ApiException(409,"此流程已處理。");
        var access=await CanAct(workflow,stage,scope.UserId,ct);
        if(!access.Allowed) throw new ApiException(403,"您不是此關卡的簽核人或有效代理人。");
        stage.Status=request.Approve?"Approved":"Rejected"; stage.ActorUserId=scope.UserId; stage.RepresentedUserId=access.Represented;
        stage.Comment=request.Comment.Trim(); stage.ActedAt=DateTime.UtcNow; workflow.UpdatedAt=DateTime.UtcNow;
        if(!request.Approve){workflow.Status="Rejected";foreach(var s in workflow.Stages.Where(x=>x.Status=="Waiting"))s.Status="Skipped";}
        else {var next=workflow.Stages.OrderBy(x=>x.Position).FirstOrDefault(x=>x.Status=="Waiting");if(next is null)workflow.Status="Approved";else{next.Status="Pending";next.DueAt=DateTime.UtcNow.AddHours(next.DueHours);}}
        if(workflow.Status!="Pending") workflow.CompletedAt=DateTime.UtcNow;
        if(workflow.Kind=="Enrollment"){
            var a=await db.Approvals.Include(x=>x.Course).SingleAsync(x=>x.Id==workflow.RelatedId,ct);
            await CourseWriteLock.AcquireAsync(db,a.CourseId,ct);
            if(a.Status!="Pending")throw new ApiException(409,"原申請已取消或處理。");
            if(workflow.Status=="Approved"){
                if(a.RequestedCost!=a.Course.Cost) throw new ApiException(409,"課程費用已變更，請撤回後重新申請。");
                await training.Register(a.CourseId,a.EmployeeId,true,ct,a.WaitlistEntryId);
                if(a.WaitlistEntryId.HasValue){var w=await db.Waitlist.FindAsync([a.WaitlistEntryId.Value],ct);w!.Status="Accepted";}
            }
            a.Status=workflow.Status; a.Comment=request.Comment.Trim(); a.ReviewerId=scope.UserId; a.ReviewedAt=workflow.CompletedAt;
            if(workflow.Status=="Rejected" && a.WaitlistEntryId.HasValue){var w=await db.Waitlist.FindAsync([a.WaitlistEntryId.Value],ct);if(w is not null)w.Status="Cancelled";}
        }else if(workflow.Kind=="Expense"){
            var expense=await db.Expenses.Include(x=>x.Employee).SingleAsync(x=>x.Id==workflow.RelatedId,ct);
            if(workflow.Status=="Approved")await budgets.ApproveExpense(expense,scope.UserId,ct);
            if(workflow.Status=="Rejected"){expense.Status="Rejected";expense.ReviewerId=scope.UserId;expense.ReviewedAt=DateTime.UtcNow;}
            expense.Comment=request.Comment.Trim();
        }
        await Notify(workflow,ct);
        if(workflow.Status!="Pending")await training.NotifyEmployee(workflow.EmployeeId,"簽核流程已完成",$"「{workflow.Title}」：{(workflow.Status=="Approved"?"已核准":"已退回")}",$"flow-done:{workflow.Id}",ct);
        return workflow;
    }
    public async Task Notify(ApprovalWorkflow workflow,CancellationToken ct)
    {
        var stage=workflow.Stages.FirstOrDefault(x=>x.Status=="Pending");if(stage is null)return;
        var ids=stage.ApproverKind=="HR"?await db.AppUsers.Where(x=>x.Role=="HR" && x.IsActive && x.IsApproved).Select(x=>x.Id).ToListAsync(ct):stage.ApproverUserId.HasValue?[stage.ApproverUserId.Value]:new List<int>();
        var now=DateTime.UtcNow;ids.AddRange(await db.Delegations.Where(x=>ids.Contains(x.PrincipalUserId)&&x.IsActive&&x.StartsAt<=now&&x.EndsAt>now).Select(x=>x.DelegateUserId).ToListAsync(ct));
        foreach(var id in ids.Distinct())await training.Notify(id,"待處理簽核關卡",$"「{workflow.Title}」等待「{stage.Name}」，期限 {stage.DueAt:yyyy/MM/dd HH:mm} UTC。",$"flow:{workflow.Id}:{stage.Position}","/workflows",ct);
    }
}
