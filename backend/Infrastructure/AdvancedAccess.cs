using Microsoft.EntityFrameworkCore;
using TrainingManagement.Api.Data;
using TrainingManagement.Api.Models;
namespace TrainingManagement.Api.Infrastructure;
public sealed class AdvancedAccess(TrainingDbContext db, DataScope scope)
{
    public async Task<int> Self(CancellationToken ct) => await db.AppUsers.Where(x => x.Id == scope.UserId && x.Employee != null && x.Employee.IsActive)
        .Select(x => x.EmployeeId).SingleOrDefaultAsync(ct) ?? throw new ApiException(409,"請先綁定在職員工資料。");
    public async Task<IQueryable<Employee>> People(CancellationToken ct)
    {
        if (scope.User.IsInRole("Instructor")) { var id = await Self(ct); return db.Employees.Where(x => x.Id == id); }
        return await scope.Employees(ct);
    }
    public async Task ReviewEmployee(int employeeId, CancellationToken ct)
    {
        if (!scope.IsGlobal && !scope.User.IsInRole("Manager")) throw new ApiException(403,"需要主管或人資審核權限。");
        await scope.RequireEmployee(employeeId,ct);
        if (await db.AppUsers.AnyAsync(x => x.Id == scope.UserId && x.EmployeeId == employeeId,ct)) throw new ApiException(403,"不可審核自己的申請。");
    }
    public static async Task<(string Name,string Type,byte[] Bytes)> Evidence(IFormFile file, CancellationToken ct)
    {
        if (file.Length is <=0 or >10_000_000) throw new ApiException(400,"附件須為 1 byte 至 10 MB。");
        var name = Path.GetFileName(file.FileName); var extension = Path.GetExtension(name).ToLowerInvariant();
        if (name.Length > 200 || extension is not (".pdf" or ".png" or ".jpg" or ".jpeg")) throw new ApiException(400,"附件僅接受 PDF、PNG 或 JPEG。");
        using var stream = new MemoryStream(); await file.CopyToAsync(stream,ct); var b = stream.ToArray();
        var valid = extension switch { ".pdf" => b.AsSpan().StartsWith("%PDF-"u8), ".png" => b.AsSpan().StartsWith(new byte[]{137,80,78,71,13,10,26,10}), _ => b.Length>3 && b[0]==255 && b[1]==216 && b[2]==255 };
        if (!valid) throw new ApiException(400,"附件內容與檔案格式不符。");
        return (name,extension==".pdf"?"application/pdf":extension==".png"?"image/png":"image/jpeg",b);
    }
    public static Task Lock(TrainingDbContext db,string resource,CancellationToken ct) => db.Database.ExecuteSqlInterpolatedAsync($"DECLARE @r int; EXEC @r=sys.sp_getapplock @Resource={resource},@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=10000; IF @r<0 THROW 51001,'Advanced transaction lock timeout',1;",ct);
}
