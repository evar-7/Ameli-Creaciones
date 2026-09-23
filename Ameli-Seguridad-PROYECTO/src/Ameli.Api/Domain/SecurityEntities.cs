namespace Ameli.Api.Domain;

public sealed class AppRole
{
    public string Name { get; set; } = "";
    public bool IsActive { get; set; } = true;
}
public sealed class AppUser
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    public string NormalizedEmail { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string RoleName { get; set; } = "";
    public bool IsInternal { get; set; }
    public bool IsActive { get; set; } = true;
    public int FailedAccessCount { get; set; }
    public DateTimeOffset? LockedUntilUtc { get; set; }
    public DateTimeOffset? LastLoginUtc { get; set; }
    public int SecurityVersion { get; set; } = 1;
    public DateTimeOffset CreatedAtUtc { get; set; }
}
public sealed class AuthSession
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public int SecurityVersion { get; set; }
    public string RefreshTokenHash { get; set; } = "";
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset LastActivityAtUtc { get; set; }
    public DateTimeOffset AbsoluteExpiresAtUtc { get; set; }
    public DateTimeOffset? RevokedAtUtc { get; set; }
    public string Device { get; set; } = "";
}
public sealed class PasswordReset
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public string TokenHash { get; set; } = "";
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset ExpiresAtUtc { get; set; }
    public DateTimeOffset? UsedAtUtc { get; set; }
}
public sealed class RecoveryAttempt
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string AccountKey { get; set; } = "";
    public string Origin { get; set; } = "";
    public DateTimeOffset CreatedAtUtc { get; set; }
}
public sealed class SecurityEvent
{
    public long Id { get; set; }
    public DateTimeOffset OccurredAtUtc { get; set; }
    public Guid? ActorUserId { get; set; }
    public Guid? SubjectUserId { get; set; }
    public string ActorRole { get; set; } = "";
    public string Action { get; set; } = "";
    public string Outcome { get; set; } = "";
    public string Origin { get; set; } = "";
    public string CorrelationId { get; set; } = "";
    public string Detail { get; set; } = "";
}
public sealed class OutgoingEmail
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Recipient { get; set; } = "";
    public string Subject { get; set; } = "";
    public string ProtectedBody { get; set; } = "";
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset NextAttemptAtUtc { get; set; }
    public DateTimeOffset? SentAtUtc { get; set; }
    public int Attempts { get; set; }
}

