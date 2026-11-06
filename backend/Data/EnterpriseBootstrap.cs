using Microsoft.EntityFrameworkCore;
namespace TrainingManagement.Api.Data;
public static class EnterpriseBootstrap
{
    public static async Task Initialize(TrainingDbContext db, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlRawAsync("""
        DECLARE @lock int;
        EXEC @lock=sys.sp_getapplock @Resource=N'training-enterprise-bootstrap',@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=10000;
        IF @lock < 0 THROW 51001,'Enterprise bootstrap lock timeout',1;
        INSERT INTO Departments (Name)
        SELECT DISTINCT e.Department FROM Employees e WHERE NOT EXISTS (SELECT 1 FROM Departments d WHERE d.Name=e.Department);
        UPDATE e SET RecognizedHours=c.DurationHours,RecognizedCost=c.Cost,CourseTitleSnapshot=c.Title,
            DepartmentSnapshot=p.Department,CourseRevisionSnapshot=c.Revision
        FROM Enrollments e JOIN Courses c ON e.CourseId=c.Id JOIN Employees p ON e.EmployeeId=p.Id
        WHERE e.Status='Completed' AND e.RecognizedHours IS NULL;
        INSERT INTO Certificates (EnrollmentId,Attempt,EmployeeId,Serial,EmployeeName,CourseTitle,Hours,IssuedAt)
        SELECT e.Id,e.Attempt,e.EmployeeId,UPPER(REPLACE(CONVERT(varchar(36),NEWID()),'-','')),p.Name,
            COALESCE(e.CourseTitleSnapshot,c.Title),COALESCE(e.RecognizedHours,c.DurationHours),COALESCE(e.CompletedAt,SYSUTCDATETIME())
        FROM Enrollments e JOIN Employees p ON e.EmployeeId=p.Id JOIN Courses c ON e.CourseId=c.Id
        WHERE e.Status='Completed' AND NOT EXISTS(SELECT 1 FROM Certificates t WHERE t.EnrollmentId=e.Id AND t.Attempt=e.Attempt);
        """, ct);
        await tx.CommitAsync(ct);
    }
}
