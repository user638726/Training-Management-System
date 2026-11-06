using Microsoft.EntityFrameworkCore;
using TrainingManagement.Api.Data;
using TrainingManagement.Api.Models;
namespace TrainingManagement.Api.Infrastructure;
public sealed class WaitlistService(TrainingDbContext db,TrainingService training)
{
    // The caller owns CourseWriteLock and an SQL transaction.
    public async Task Promote(int courseId,CancellationToken ct)
    {
        var course=await db.Courses.FindAsync([courseId],ct);if(course is null||course.Status!=CourseStatus.Published)return;
        var now=DateTime.UtcNow;
        var expired=await db.Waitlist.Where(x=>x.CourseId==courseId&&x.Status=="Offered"&&x.OfferExpiresAt<=now).ToListAsync(ct);
        foreach(var e in expired){
            e.Status="Expired";
            foreach(var a in await db.Approvals.Where(x=>x.WaitlistEntryId==e.Id&&x.Status=="Pending").ToListAsync(ct)){
                a.Status="Expired";if(a.WorkflowId.HasValue){var f=await db.Workflows.Include(x=>x.Stages).SingleAsync(x=>x.Id==a.WorkflowId,ct);f.Status="Expired";f.UpdatedAt=now;foreach(var s in f.Stages.Where(x=>x.Status=="Pending"||x.Status=="Waiting"))s.Status="Skipped";}
            }
        }
        await db.SaveChangesAsync(ct);
        var used=await db.Enrollments.CountAsync(x=>x.CourseId==courseId&&x.Status!=EnrollmentStatus.Cancelled,ct)
            +await db.Waitlist.CountAsync(x=>x.CourseId==courseId&&x.Status=="Offered"&&x.OfferExpiresAt>now,ct);
        var slots=Math.Max(0,course.Capacity-used);if(slots==0)return;
        var waiting=await db.Waitlist.Include(x=>x.Employee).Where(x=>x.CourseId==courseId&&x.Status=="Waiting").OrderBy(x=>x.QueuedAt).ThenBy(x=>x.Id).ToListAsync(ct);
        foreach(var e in waiting){
            if(!e.Employee.IsActive){e.Status="Cancelled";continue;}
            if(slots--<=0)break;
            e.Status="Offered";e.OfferedAt=now;e.OfferExpiresAt=now.AddHours(24);
            await training.NotifyEmployee(e.EmployeeId,"候補名額可確認",$"「{course.Title}」已有名額，請於 24 小時內確認；需要簽核的課程也須在此期限內完成。",$"waitlist-offer:{e.Id}",ct);
        }
    }
}
