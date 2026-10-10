using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TrainingManagement.Api.Models;

namespace TrainingManagement.Api.Data;

public static class BootstrapAdmin
{
    public static async Task SeedAsync(TrainingDbContext db, IConfiguration configuration,
        IPasswordHasher<AppUser> hasher)
    {
        if (await db.AppUsers.AnyAsync()) return;
        var username = configuration["BootstrapAdmin:Username"]?.Trim();
        var password = configuration["BootstrapAdmin:Password"];
        var displayName = configuration["BootstrapAdmin:DisplayName"]?.Trim();
        if (string.IsNullOrWhiteSpace(username) || username.Length > 80
            || string.IsNullOrWhiteSpace(password) || password.Length is < 12 or > 256
            || string.IsNullOrWhiteSpace(displayName) || displayName.Length > 100)
            throw new InvalidOperationException("全新資料庫需要 BootstrapAdmin:Username、DisplayName 與 12 至 256 字元密碼；不提供預設帳密。");
        var user = new AppUser
        {
            Username = username, NormalizedUsername = username.ToUpperInvariant(),
            DisplayName = displayName, Role = "Admin", IsActive = true
        };
        user.PasswordHash = hasher.HashPassword(user, password);
        db.AppUsers.Add(user);
        await db.SaveChangesAsync();
    }
}
