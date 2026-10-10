using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace TrainingManagement.Api.Models;

public sealed class LoginRequest
{
    [Required(ErrorMessage = "請輸入帳號。"), StringLength(80)]
    public string Username { get; set; } = "";
    [Required(ErrorMessage = "請輸入密碼。"), StringLength(256)]
    public string Password { get; set; } = "";
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class RegisterRequest
{
    private string username = "";
    private string displayName = "";

    [Required(ErrorMessage = "請輸入帳號。"), StringLength(80, MinimumLength = 3, ErrorMessage = "帳號長度必須介於 3 與 80 字元之間。"),
     RegularExpression(@"^[A-Za-z0-9._-]+$", ErrorMessage = "帳號只能包含英文字母、數字、點、底線與連字號。")]
    public string Username { get => username; set => username = value?.Trim() ?? ""; }

    [Required(ErrorMessage = "請輸入顯示名稱。"), StringLength(100, ErrorMessage = "顯示名稱不得超過 100 字元。")]
    public string DisplayName { get => displayName; set => displayName = value?.Trim() ?? ""; }

    [Required(ErrorMessage = "請輸入密碼。"), StringLength(256, MinimumLength = 12, ErrorMessage = "密碼長度必須介於 12 與 256 字元之間。")]
    public string Password { get; set; } = "";

    [Required(ErrorMessage = "請再次輸入密碼。"), Compare(nameof(Password), ErrorMessage = "兩次輸入的密碼必須完全一致。")]
    public string ConfirmPassword { get; set; } = "";
}

public sealed record UserResponse(int Id, string Username, string DisplayName, string Role, int? EmployeeId)
{
    public static UserResponse From(AppUser user) => new(user.Id, user.Username, user.DisplayName, user.Role, user.EmployeeId);
}

public sealed record LoginResponse(string AccessToken, string TokenType, DateTime ExpiresAt, UserResponse User);
