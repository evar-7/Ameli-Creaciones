namespace Ameli.Api.Application;

public sealed class SecurityOptions
{
    public int FailedAttempts { get; set; } = 5;
    public int LockoutMinutes { get; set; } = 15;
    public int IdleMinutes { get; set; } = 15;
    public int ResetMinutes { get; set; } = 15;
    public int AccessTokenMinutes { get; set; } = 5;
    public int AbsoluteSessionHours { get; set; } = 8;
    public int RecoveryAccountLimit { get; set; } = 3;
    public int RecoveryOriginLimit { get; set; } = 10;
    public string WebBaseUrl { get; set; } = "https://localhost:7240";
}
public sealed class JwtOptions
{
    public string Issuer { get; set; } = "Ameli.Security";
    public string Audience { get; set; } = "Ameli.Clients";
    public string SigningKey { get; set; } = "";
}
public sealed record RequestOrigin(string Ip, string Device, string CorrelationId);
public sealed record Actor(Guid UserId, Guid SessionId);
public sealed class SecurityFault(string code, string message, int status = 400) : Exception(message)
{
    public string Code { get; } = code;
    public int Status { get; } = status;
}

