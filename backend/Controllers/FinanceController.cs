using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TrainingManagement.Api.Data;
using TrainingManagement.Api.Infrastructure;
using TrainingManagement.Api.Models;
namespace TrainingManagement.Api.Controllers;
[ApiController,Route("api/advanced")]
public sealed class FinanceController(TrainingDbContext db,DataScope scope,AdvancedAccess access,MutationGate gate,BudgetService budgets,WorkflowService workflows,TrainingService training):ControllerBase
{
    [HttpGet("budgets")]
    public async Task<IActionResult> List(CancellationToken ct){var rows=await db.Budgets.Where(x=>scope.IsGlobal||x.OwnerUserId==scope.UserId).OrderByDescending(x=>x.Year).ThenBy(x=>x.Id).Take(500).ToListAsync(ct);var result=new List<object>();foreach(var b in rows){var used=await budgets.Used(b.Id,ct);result.Add(new{b.Id,b.Name,b.Year,b.Department,b.OwnerUserId,b.Allocated,b.Currency,b.IsActive,b.ExpensePolicyId,used,available=b.Allocated-used,rowVersion=Convert.ToBase64String(b.RowVersion)});}return Ok(result);}
    [HttpGet("budgets/available")]
    public async Task<IActionResult> Available(CancellationToken ct){var eid=await access.Self(ct);var department=await db.Employees.Where(x=>x.Id==eid).Select(x=>x.Department).SingleAsync(ct);return Ok(await db.Budgets.Where(x=>x.IsActive&&(x.Department==null||x.Department==department)).OrderByDescending(x=>x.Year).Select(x=>new{x.Id,x.Name,x.Year,x.Currency}).ToListAsync(ct));}
    [HttpPost("budgets"),Authorize(Roles=Roles.Administrators)]
    public async Task<IActionResult> Create(BudgetRequest r,CancellationToken ct){await Validate(r,ct);var b=new TrainingBudget();Apply(b,r);db.Budgets.Add(b);await db.SaveChangesAsync(ct);return StatusCode(201,new{b.Id});}
    [HttpPut("budgets/{id:int}"),Authorize(Roles=Roles.Administrators)]
    public async Task<IActionResult> Update(int id,BudgetRequest r,CancellationToken ct){using var write=await gate.EnterAsync(ct);await using var tx=await db.Database.BeginTransactionAsync(ct);await AdvancedAccess.Lock(db,$"budget:{id}",ct);await Validate(r,ct);var b=await db.Budgets.FindAsync([id],ct)??throw new ApiException(404,"找不到預算。");if(string.IsNullOrWhiteSpace(r.RowVersion))throw new ApiException(400,"請提供版本。");DataScope.CheckVersion(b.RowVersion,r.RowVersion);var used=await budgets.Used(id,ct);if(r.Allocated<used)throw new ApiException(409,"額度不可小於已預留與支出的金額。");if(used>0&&(b.Year!=r.Year||b.Currency!=r.Currency||b.Department!=r.Department))throw new ApiException(409,"已有使用紀錄的預算不可變更年度、幣別或部門。");Apply(b,r);await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);return NoContent();}
    private async Task Validate(BudgetRequest r,CancellationToken ct){if(!await db.AppUsers.AnyAsync(x=>x.Id==r.OwnerUserId&&x.IsActive&&x.IsApproved,ct))throw new ApiException(400,"預算負責人未啟用。");if(!string.IsNullOrWhiteSpace(r.Department)&&!await db.Departments.AnyAsync(x=>x.Name==r.Department.Trim(),ct))throw new ApiException(400,"部門不存在。");if(r.ExpensePolicyId.HasValue&&!await db.ApprovalPolicies.AnyAsync(x=>x.Id==r.ExpensePolicyId&&x.IsActive,ct))throw new ApiException(400,"費用簽核流程不存在。");}
    private static void Apply(TrainingBudget b,BudgetRequest r){b.Name=r.Name.Trim();b.Year=r.Year;b.Department=string.IsNullOrWhiteSpace(r.Department)?null:r.Department.Trim();b.OwnerUserId=r.OwnerUserId;b.Allocated=r.Allocated;b.Currency=r.Currency;b.ExpensePolicyId=r.ExpensePolicyId;b.IsActive=r.IsActive;}
    [HttpGet("budgets/{id:int}/ledger")]
    public async Task<IActionResult> Ledger(int id,CancellationToken ct){if(!await db.Budgets.AnyAsync(x=>x.Id==id&&(scope.IsGlobal||x.OwnerUserId==scope.UserId),ct))throw new ApiException(404,"找不到預算。");return Ok(await db.Commitments.Where(x=>x.BudgetId==id).OrderByDescending(x=>x.Id).Take(1000).Select(x=>new{x.Id,x.EnrollmentId,x.Attempt,x.Amount,x.Status,x.CreatedAt,employeeName=x.Enrollment.Employee.Name,courseTitle=x.Enrollment.Course.Title}).ToListAsync(ct));}
    private async Task<IQueryable<ExpenseClaim>> Visible(CancellationToken ct){var now=DateTime.UtcNow;var delegates=await db.Delegations.Where(x=>x.DelegateUserId==scope.UserId&&x.IsActive&&x.StartsAt<=now&&x.EndsAt>now).Select(x=>x.PrincipalUserId).ToArrayAsync(ct);return db.Expenses.Where(x=>scope.IsGlobal||x.Budget.OwnerUserId==scope.UserId||db.AppUsers.Any(u=>u.Id==scope.UserId&&u.EmployeeId==x.EmployeeId)||db.ApprovalStages.Any(s=>s.WorkflowId==x.WorkflowId&&s.Status=="Pending"&&(s.ApproverUserId==scope.UserId||delegates.Contains(s.ApproverUserId??0))));}
    [HttpGet("expenses")]
    public async Task<IActionResult> Expenses(CancellationToken ct)=>Ok(await(await Visible(ct)).OrderByDescending(x=>x.Id).Take(500).Select(x=>new{x.Id,x.Title,x.Vendor,x.Amount,x.EmployeeId,employeeName=x.Employee.Name,x.BudgetId,budgetName=x.Budget.Name,currency=x.Budget.Currency,ownerUserId=x.Budget.OwnerUserId,x.Status,x.EnrollmentId,x.WorkflowId,x.ReceiptName,x.CreatedAt,x.Comment,x.ReviewedAt,rowVersion=Convert.ToBase64String(x.RowVersion)}).ToListAsync(ct));
    [HttpPost("expenses"),RequestSizeLimit(11_000_000)]
    public async Task<IActionResult> Submit([FromForm]ExpenseUpload r,CancellationToken ct)
    {
        var eid=await access.Self(ct);var file=await AdvancedAccess.Evidence(r.Receipt,ct);using var write=await gate.EnterAsync(ct);await using var tx=await db.Database.BeginTransactionAsync(ct);
        var employee=await db.Employees.FindAsync([eid],ct);var b=await db.Budgets.FindAsync([r.BudgetId],ct)??throw new ApiException(404,"預算不存在。");
        if(!b.IsActive||b.Year!=DateTime.UtcNow.AddHours(8).Year||(b.Department!=null&&b.Department!=employee!.Department))throw new ApiException(409,"預算年度、啟用狀態或部門不符。");
        if(r.EnrollmentId.HasValue&&!await db.Enrollments.AnyAsync(x=>x.Id==r.EnrollmentId&&x.EmployeeId==eid&&x.Status!=EnrollmentStatus.Cancelled,ct))throw new ApiException(400,"報名紀錄不屬於本人或已取消。");
        if(r.EnrollmentId.HasValue&&await db.Commitments.AnyAsync(x=>x.EnrollmentId==r.EnrollmentId&&x.Enrollment.Attempt==x.Attempt&&x.BudgetId!=b.Id&&x.Status!="Released",ct))throw new ApiException(409,"核銷預算須與該次報名的預留來源一致。");
        var e=new ExpenseClaim{EmployeeId=eid,BudgetId=b.Id,EnrollmentId=r.EnrollmentId,Title=r.Title.Trim(),Vendor=r.Vendor.Trim(),Amount=r.Amount,ReceiptName=file.Name,ReceiptType=file.Type,Receipt=file.Bytes};db.Expenses.Add(e);await db.SaveChangesAsync(ct);
        var f=await workflows.Initialize("Expense",e.Id,eid,b.ExpensePolicyId,e.Amount,e.Title,b.OwnerUserId,ct);e.WorkflowId=f?.Id;
        if(f is null)await training.Notify(b.OwnerUserId,"外訓費用待審核",$"「{e.Title}」申請 {e.Amount} {b.Currency}。",$"expense:{e.Id}","/budgets",ct);
        await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);return StatusCode(201,new{e.Id,e.Status,e.WorkflowId});
    }
    [HttpPost("expenses/{id:int}/decision")]
    public async Task<IActionResult> Decide(int id,ApprovalDecision r,CancellationToken ct)
    {
        using var write=await gate.EnterAsync(ct);await using var tx=await db.Database.BeginTransactionAsync(ct);
        var e=await db.Expenses.Include(x=>x.Budget).Include(x=>x.Employee).SingleOrDefaultAsync(x=>x.Id==id,ct)??throw new ApiException(404,"找不到費用申請。");
        if(!scope.IsGlobal&&e.Budget.OwnerUserId!=scope.UserId)throw new ApiException(403,"僅預算負責人或人資可審核。");
        if(await db.AppUsers.AnyAsync(x=>x.Id==scope.UserId&&x.EmployeeId==e.EmployeeId,ct))throw new ApiException(403,"不可審核自己的核銷。");
        DataScope.CheckVersion(e.RowVersion,r.RowVersion);if(e.WorkflowId.HasValue)throw new ApiException(409,"請從多階簽核流程處理。");if(e.Status!="Submitted")throw new ApiException(409,"申請已處理。");
        if(r.Approve)await budgets.ApproveExpense(e,scope.UserId,ct);else{e.Status="Rejected";e.ReviewerId=scope.UserId;e.ReviewedAt=DateTime.UtcNow;}e.Comment=r.Comment.Trim();await training.NotifyEmployee(e.EmployeeId,"費用審核結果",$"「{e.Title}」{(r.Approve?"已核准":"已退回")}",$"expense-result:{e.Id}",ct);await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);return NoContent();
    }
    [HttpPost("expenses/{id:int}/paid")]
    public async Task<IActionResult> Paid(int id,ApprovalDecision r,CancellationToken ct){var e=await db.Expenses.Include(x=>x.Budget).SingleOrDefaultAsync(x=>x.Id==id,ct)??throw new ApiException(404,"找不到核銷。");if(!scope.IsGlobal&&e.Budget.OwnerUserId!=scope.UserId)throw new ApiException(403,"沒有付款紀錄權限。");if(await db.AppUsers.AnyAsync(x=>x.Id==scope.UserId&&x.EmployeeId==e.EmployeeId,ct))throw new ApiException(403,"不可確認自己的付款。");DataScope.CheckVersion(e.RowVersion,r.RowVersion);if(e.Status!="Approved")throw new ApiException(409,"只有已核准費用可標示付款。");e.Status="Paid";await db.SaveChangesAsync(ct);return NoContent();}
    [HttpGet("expenses/{id:int}/receipt")]
    public async Task<IActionResult> Receipt(int id,CancellationToken ct){var e=await(await Visible(ct)).SingleOrDefaultAsync(x=>x.Id==id,ct)??throw new ApiException(404,"找不到附件。");return File(e.Receipt,e.ReceiptType,e.ReceiptName);}
}
