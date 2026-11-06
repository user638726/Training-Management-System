using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TrainingManagement.Api.Data;
using TrainingManagement.Api.Models;

namespace TrainingManagement.Api.Infrastructure;

public sealed class EmployeeImportService(TrainingDbContext db, DataScope scope, MutationGate gate)
{
    public async Task<ImportPreviewResult> Preview(string csv, CancellationToken ct)
    {
        var parsed = Parse(csv);
        if (parsed.Count < 2 || parsed.Count > 1001) throw new ApiException(400, "CSV 須包含標頭及 1 至 1000 筆資料。");
        var header = parsed[0].Select(x => x.Trim().TrimStart('\uFEFF')).ToArray();
        string[] required = ["employeeNumber", "name", "email", "department", "jobTitle", "isActive"];
        if (required.Any(x => !header.Contains(x)) || header.Distinct().Count() != header.Length) throw new ApiException(400, "CSV 標頭須包含 employeeNumber,name,email,department,jobTitle,isActive；可選 hiredAt。");
        string Cell(string[] row, string column) { var i = Array.IndexOf(header, column); return i >= 0 && i < row.Length ? row[i].Trim() : ""; }
        var requests = new List<(EmployeeRequest Request, List<string> Errors)>();
        foreach (var row in parsed.Skip(1)) {
            var errors = new List<string>();
            if (row.Length != header.Length) errors.Add("欄位數與標頭不一致。");
            var active = Cell(row, "isActive");
            if (!bool.TryParse(active, out var isActive)) errors.Add("isActive 須為 true 或 false。");
            var date = Cell(row, "hiredAt"); DateTime? hired = null;
            if (date.Length > 0) { if (DateTime.TryParseExact(date, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var value)) hired = value; else errors.Add("hiredAt 須為 yyyy-MM-dd。"); }
            requests.Add((new EmployeeRequest { EmployeeNumber = Cell(row, "employeeNumber").ToUpperInvariant(), Name = Cell(row, "name"),
                Email = Cell(row, "email").ToLowerInvariant(), Department = Cell(row, "department"), JobTitle = Cell(row, "jobTitle"), IsActive = isActive, HiredAt = hired }, errors));
        }
        var numbers = requests.Select(x => x.Request.EmployeeNumber).ToArray(); var emails = requests.Select(x => x.Request.Email).ToArray();
        var existing = await db.Employees.AsNoTracking().Where(x => numbers.Contains(x.EmployeeNumber) || emails.Contains(x.Email)).ToListAsync(ct);
        var rows = requests.Select((item, i) => {
            var r = item.Request; var errors = item.Errors; var validation = new List<ValidationResult>();
            Validator.TryValidateObject(r, new ValidationContext(r), validation, true); errors.AddRange(validation.Select(x => x.ErrorMessage ?? "欄位無效。"));
            if (numbers.Count(x => x.Equals(r.EmployeeNumber, StringComparison.OrdinalIgnoreCase)) > 1) errors.Add("檔案內員工編號重複。");
            if (emails.Count(x => x.Equals(r.Email, StringComparison.OrdinalIgnoreCase)) > 1) errors.Add("檔案內電子郵件重複。");
            var employee = existing.FirstOrDefault(x => x.EmployeeNumber == r.EmployeeNumber);
            if (existing.Any(x => x.Email == r.Email && x.Id != employee?.Id)) errors.Add("電子郵件已被其他員工使用。");
            return new ImportRow(i + 2, employee is null ? "Create" : "Update", r, employee is null ? null : Convert.ToBase64String(employee.RowVersion), errors.ToArray());
        }).ToArray();
        var batch = new ImportBatch { CreatedBy = scope.UserId, RowsJson = JsonSerializer.Serialize(rows) };
        db.ImportBatches.Add(batch); await db.SaveChangesAsync(ct);
        return new ImportPreviewResult(batch.Id,batch.ExpiresAt,rows.All(x=>x.Errors.Length==0),rows);
    }
    public async Task<object> Commit(Guid id, CancellationToken ct)
    {
        using var write = await gate.EnterAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        var batch = await db.ImportBatches.SingleOrDefaultAsync(x => x.Id == id && x.CreatedBy == scope.UserId && x.CommittedAt == null && x.ExpiresAt > DateTime.UtcNow, ct)
            ?? throw new ApiException(409, "預覽不存在、已匯入或已到期，請重新預覽。");
        var rows = JsonSerializer.Deserialize<ImportRow[]>(batch.RowsJson)!;
        if (rows.Any(x => x.Errors.Length > 0)) throw new ApiException(400, "請先修正預覽中的錯誤。");
        var departments = (await db.Departments.Select(x => x.Name).ToListAsync(ct)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows) {
            var r = row.Employee;
            var employee = await db.Employees.SingleOrDefaultAsync(x => x.EmployeeNumber == r.EmployeeNumber, ct);
            if (row.Action == "Create" && employee is not null || row.Action == "Update" && employee is null) throw new ApiException(409, "員工資料已變更，請重新預覽。");
            if (employee is not null) DataScope.CheckVersion(employee.RowVersion, row.RowVersion);
            else { employee = new Employee { EmployeeNumber = r.EmployeeNumber }; db.Employees.Add(employee); }
            if (await db.Employees.AnyAsync(x => x.Email == r.Email && x.Id != employee.Id, ct)) throw new ApiException(409, "電子郵件資料已變更，請重新預覽。");
            employee.Name = r.Name; employee.Email = r.Email; employee.Department = r.Department; employee.JobTitle = r.JobTitle;
            employee.IsActive = r.IsActive; if (r.HiredAt.HasValue) employee.HiredAt = r.HiredAt.Value;
            if (departments.Add(r.Department)) db.Departments.Add(new Department { Name = r.Department });
            if (!r.IsActive && employee.Id > 0) foreach (var account in await db.AppUsers.Where(x => x.EmployeeId == employee.Id).ToListAsync(ct)) {
                if (account.Id == scope.UserId) throw new ApiException(409, "匯入不可停用目前登入的帳號。");
                if (account.Role == "Admin" && (!scope.User.IsInRole("Admin") || await db.AppUsers.CountAsync(x => x.Role == "Admin" && x.IsActive && x.IsApproved, ct) <= 1))
                    throw new ApiException(409, "匯入不可停用此管理員帳號，請先確認備援管理員。");
                account.IsActive = false; account.TokenVersion++;
            }
        }
        batch.CommittedAt = DateTime.UtcNow; await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        return new { imported = rows.Length, created = rows.Count(x => x.Action == "Create"), updated = rows.Count(x => x.Action == "Update") };
    }
    public static List<string[]> Parse(string csv)
    {
        var rows = new List<string[]>(); var cells = new List<string>(); var cell = new StringBuilder(); var quoted = false; var endedQuote = false;
        void EndCell() { cells.Add(cell.ToString()); cell.Clear(); endedQuote = false; }
        void EndRow() { EndCell(); if (cells.Any(x => x.Length > 0)) rows.Add(cells.ToArray()); cells.Clear(); }
        for (var i = 0; i < csv.Length; i++) {
            var c = csv[i];
            if (quoted) {
                if (c == '"') { if (i + 1 < csv.Length && csv[i + 1] == '"') { cell.Append('"'); i++; } else { quoted = false; endedQuote = true; } }
                else cell.Append(c);
            } else if (c == '"' && cell.Length == 0 && !endedQuote) quoted = true;
            else if (c == ',') EndCell();
            else if (c is '\r' or '\n') { if (c == '\r' && i + 1 < csv.Length && csv[i + 1] == '\n') i++; EndRow(); }
            else if (endedQuote || c == '"') throw new ApiException(400, "CSV 引號格式不正確。");
            else cell.Append(c);
            if (rows.Count > 1001 || cell.Length > 5000 || cells.Count > 20) throw new ApiException(400, "CSV 超過筆數、欄位或欄位長度限制。");
        }
        if (quoted) throw new ApiException(400, "CSV 引號未結束。");
        if (cell.Length > 0 || cells.Count > 0 || endedQuote) EndRow();
        return rows;
    }
}
