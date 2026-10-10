using Microsoft.EntityFrameworkCore;
using TrainingManagement.Api.Data;

namespace TrainingManagement.Api.Infrastructure;

public static class CourseWriteLock
{
    public static async Task AcquireAsync(TrainingDbContext db, int courseId, CancellationToken cancellationToken)
    {
        // SQL Server releases this lock when the surrounding transaction ends.
        // The resource is database-scoped and protects seats across API hosts.
        var resource = $"TrainingManagement.Course.{courseId}";
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            DECLARE @result int;
            EXEC @result = sys.sp_getapplock
                @Resource = {resource}, @LockMode = 'Exclusive',
                @LockOwner = 'Transaction', @LockTimeout = 10000;
            IF @result < 0 THROW 51001, 'Unable to acquire course write lock.', 1;
            """, cancellationToken);
    }
}
