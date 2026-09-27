using System.ComponentModel.DataAnnotations;

namespace Ameli.Contracts;

public static class Roles
{
    public const string Client = "Cliente";
    public const string Administrator = "Administrador";
    public const string Logistics = "Logística";
    public static readonly string[] All = [Client, Administrator, Logistics];
    public static bool IsInternal(string role) => role is Administrator or Logistics;
    public static string Home(string role) => role switch
    {
        Administrator => "/administracion",
        Logistics => "/logistica",
        _ => "/cliente"
    };
}

public sealed class LoginRequest
{
    [Required, EmailAddress, MaxLength(254)] public string Email { get; set; } = "";
    [Required, MaxLength(128)] public string Password { get; set; } = "";
}
public sealed class ForgotPasswordRequest
{
    [Required, EmailAddress, MaxLength(254)] public string Email { get; set; } = "";
}
public sealed class ResetPasswordRequest
{
    public Guid UserId { get; set; }
    [Required, MaxLength(128)] public string Token { get; set; } = "";
    [Required, StringLength(64, MinimumLength = 8)] public string Password { get; set; } = "";
    [Required, Compare(nameof(Password))] public string ConfirmPassword { get; set; } = "";
}
public sealed class ValidateResetRequest
{
    public Guid UserId { get; set; }
    [Required, MaxLength(128)] public string Token { get; set; } = "";
}
public sealed class RevokeUserSessionsRequest
{
    [Required, StringLength(500, MinimumLength = 5)] public string Reason { get; set; } = "";
}
public sealed class ChangePasswordRequest
{
    [Required, MaxLength(128)] public string CurrentPassword { get; set; } = "";
    [Required, StringLength(64, MinimumLength = 8)] public string Password { get; set; } = "";
    [Required, Compare(nameof(Password))] public string ConfirmPassword { get; set; } = "";
}
public sealed class RefreshRequest
{
    [Required, MaxLength(128)] public string RefreshToken { get; set; } = "";
}
public sealed class ChangeRoleRequest
{
    [Required, MaxLength(30)] public string Role { get; set; } = "";
}
public sealed class ChangeStatusRequest
{
    public bool IsActive { get; set; }
    [Required, StringLength(500, MinimumLength = 5)] public string Reason { get; set; } = "";
}
public sealed class UnlockRequest
{
    [Required, StringLength(500, MinimumLength = 5)] public string Reason { get; set; } = "";
    [Required, StringLength(500, MinimumLength = 5)] public string IdentityEvidence { get; set; } = "";
}
public sealed record UserView(Guid Id, string Name, string Email, string Role, bool IsActive,
    DateTimeOffset? LockedUntilUtc, DateTimeOffset? LastLoginUtc);
public sealed record SessionView(Guid Id, DateTimeOffset CreatedAtUtc, DateTimeOffset LastActivityAtUtc,
    DateTimeOffset IdleExpiresAtUtc, DateTimeOffset AbsoluteExpiresAtUtc, string Device, bool IsCurrent);
public sealed record SessionStatus(UserView User, SessionView Session);
public sealed record TokenResponse(string AccessToken, string RefreshToken,
    DateTimeOffset AccessTokenExpiresAtUtc, SessionStatus Status);
public sealed record ApiMessage(string Message);
public sealed record ApiError(string Code, string Message, Dictionary<string, string[]>? Errors = null, Guid? ExistingId = null, InternalAccountView? Current = null);
