using Microsoft.EntityFrameworkCore;
using TrainingManagement.Api.Data;
using TrainingManagement.Api.Models;
namespace TrainingManagement.Api.Infrastructure;
public sealed class ScheduleService(TrainingDbContext db)
{
    public async Task EnsureAvailable(Course course,int employeeId,CancellationToken ct)
    {
        if(!course.CheckScheduleConflicts)return;
        var meetings=await db.Meetings.Where(x=>x.CourseId==course.Id).Select(x=>new{Start=x.StartsAt,End=x.EndsAt}).ToListAsync(ct);
        if(meetings.Count==0)meetings.Add(new{Start=course.StartDate,End=course.EndDate});
        foreach(var meeting in meetings){
            var conflict=await db.Enrollments.Where(x=>x.EmployeeId==employeeId&&x.CourseId!=course.Id&&x.Status!=EnrollmentStatus.Cancelled&&x.Course.Status!=CourseStatus.Cancelled)
                .AnyAsync(x=>db.Meetings.Any(m=>m.CourseId==x.CourseId&&m.StartsAt<meeting.End&&m.EndsAt>meeting.Start)
                    ||(!db.Meetings.Any(m=>m.CourseId==x.CourseId)&&x.Course.StartDate<meeting.End&&x.Course.EndDate>meeting.Start),ct);
            if(conflict)throw new ApiException(409,"課程時間與員工既有報名衝突，請選擇其他班次。");
        }
    }
}
