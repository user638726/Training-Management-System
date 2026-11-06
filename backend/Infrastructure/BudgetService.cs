using Microsoft.EntityFrameworkCore;
using TrainingManagement.Api.Data;
using TrainingManagement.Api.Models;
namespace TrainingManagement.Api.Infrastructure;
public sealed class BudgetService(TrainingDbContext db)
{
    public async Task<decimal> Used(int budgetId,CancellationToken ct)
    {
        var commitments = await db.Commitments.Where(x => x.BudgetId==budgetId && (x.Status=="Reserved" || x.Status=="Consumed")).SumAsync(x => x.Amount,ct);
        var expenses = await db.Expenses.Where(x => x.BudgetId==budgetId && (x.Status=="Approved" || x.Status=="Paid")).SumAsync(x => x.Amount,ct);
        return commitments + expenses;
    }
    public async Task Reserve(Enrollment e,CancellationToken ct)
    {
        if (e.Course.BudgetId is not int id) return;
        await AdvancedAccess.Lock(db,$"budget:{id}",ct);
        var b = await db.Budgets.FindAsync([id],ct) ?? throw new ApiException(409,"課程預算不存在。");
        if (!b.IsActive || b.Year!=e.Course.StartDate.AddHours(8).Year || (b.Department!=null && b.Department!=e.Employee.Department))
            throw new ApiException(409,"課程預算未啟用、年度或部門不符。");
        if (await Used(id,ct)+e.Course.Cost>b.Allocated) throw new ApiException(409,"訓練預算不足。");
        db.Commitments.Add(new BudgetCommitment{BudgetId=id,Enrollment=e,Attempt=e.Attempt,Amount=e.Course.Cost});
    }
    public async Task Release(Enrollment e,CancellationToken ct)
    {
        var c = db.Commitments.Local.FirstOrDefault(x=>x.EnrollmentId==e.Id&&x.Attempt==e.Attempt) ?? await db.Commitments.SingleOrDefaultAsync(x => x.EnrollmentId==e.Id && x.Attempt==e.Attempt,ct);
        if (c is null || c.Status=="Settled" || c.Status=="Released") return;
        await AdvancedAccess.Lock(db,$"budget:{c.BudgetId}",ct); c.Status="Released";
    }
    public async Task<decimal?> Consume(Enrollment e,CancellationToken ct)
    {
        var c = await db.Commitments.SingleOrDefaultAsync(x => x.EnrollmentId==e.Id && x.Attempt==e.Attempt,ct);
        if (c is null) return null;
        await AdvancedAccess.Lock(db,$"budget:{c.BudgetId}",ct);
        if (c.Status=="Reserved") c.Status="Consumed";
        return c.Amount;
    }
    public async Task ApproveExpense(ExpenseClaim e,int reviewer,CancellationToken ct)
    {
        await AdvancedAccess.Lock(db,$"budget:{e.BudgetId}",ct);
        var b = await db.Budgets.FindAsync([e.BudgetId],ct) ?? throw new ApiException(409,"找不到預算。");
        if (!b.IsActive || (b.Department!=null && b.Department!=e.Employee.Department)) throw new ApiException(409,"預算未啟用或員工部門不符。");
        if (e.Status!="Submitted") throw new ApiException(409,"此費用已處理。");
        BudgetCommitment? commitment=null;
        if(e.EnrollmentId.HasValue){
            var enrollment=await db.Enrollments.FindAsync([e.EnrollmentId.Value],ct) ?? throw new ApiException(409,"找不到報名。");
            commitment=await db.Commitments.SingleOrDefaultAsync(x=>x.EnrollmentId==enrollment.Id && x.Attempt==enrollment.Attempt && x.BudgetId==b.Id,ct);
            if(await db.Expenses.AnyAsync(x=>x.Id!=e.Id && x.EnrollmentId==e.EnrollmentId && (x.Status=="Approved" || x.Status=="Paid"),ct)) throw new ApiException(409,"此報名已有核准的核銷。");
        }
        var reserved = commitment is not null && (commitment.Status=="Reserved" || commitment.Status=="Consumed") ? commitment.Amount : 0;
        if(await Used(b.Id,ct)-reserved+e.Amount>b.Allocated) throw new ApiException(409,"預算不足，請先調整額度。");
        if(commitment is not null && reserved>0) commitment.Status="Settled";
        e.Status="Approved"; e.ReviewerId=reviewer; e.ReviewedAt=DateTime.UtcNow;
    }
}
