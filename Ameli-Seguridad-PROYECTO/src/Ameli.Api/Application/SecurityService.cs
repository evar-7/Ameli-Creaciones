using Ameli.Api.Domain;
using Ameli.Api.Infrastructure;
using Ameli.Contracts;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;

namespace Ameli.Api.Application;

public sealed class DummyPassword(IPasswordHasher<AppUser> hasher)
{
    public AppUser User { get; } = new();
    public string Hash { get; } = hasher.HashPassword(new AppUser(), TokenIssuer.RandomToken());
}

public sealed partial class SecurityService(SecurityDbContext db, SqlSecurityTransaction transactions,
    IPasswordHasher<AppUser> passwords, DummyPassword dummy, TokenIssuer tokens,
    IDataProtectionProvider protection, IOptions<SecurityOptions> options, TimeProvider clock, AuditIntegrity signer, ILogger<SecurityService> logger)
{
    private readonly SecurityOptions settings = options.Value;
    private readonly IDataProtector emailProtector = protection.CreateProtector("Ameli.EmailOutbox.v1");
    private const string LoginMessage = "No se pudo iniciar sesión. Revisa tus credenciales o intenta más tarde.";
    public const string RecoveryMessage = "Si la cuenta puede recuperar el acceso, recibirás instrucciones en tu correo.";
    private DateTimeOffset Now => clock.GetUtcNow();
    public static string NormalizeEmail(string email) => email.Trim().ToUpperInvariant();
    public static bool IsStrongPassword(string password) => password.Length is >= 8 and <= 64
        && password.Any(char.IsUpper) && password.Any(char.IsLower)
        && password.Any(char.IsDigit) && password.Any(c => !char.IsLetterOrDigit(c) && !char.IsWhiteSpace(c));

    public async Task<TokenResponse> LoginAsync(LoginRequest request, RequestOrigin origin, CancellationToken ct)
    {
        var email = NormalizeEmail(request.Email);
        var userId = await db.Users.AsNoTracking().Where(x => x.NormalizedEmail == email)
            .Select(x => (Guid?)x.Id).SingleOrDefaultAsync(ct);
        if (userId is null)
        {
            passwords.VerifyHashedPassword(dummy.User, dummy.Hash, request.Password);
            Audit("login", "Fallido", origin, detail: "Credenciales no aceptadas");
            await db.SaveChangesAsync(ct);
            throw new SecurityFault("invalid_login", LoginMessage, 401);
        }
        await using var tx = await transactions.BeginAsync([SqlSecurityTransaction.User(userId.Value)], ct);
        var user = await db.Users.SingleAsync(x => x.Id == userId, ct);
        var verified = passwords.VerifyHashedPassword(user, user.PasswordHash, request.Password);
        if (!user.IsActive || !await IsRoleActiveAsync(user.RoleName, ct) || user.LockedUntilUtc > Now)
            throw await FailAsync(tx, "login", "invalid_login", LoginMessage, origin, user, ct, 401);

        if (user.LockedUntilUtc is not null && user.LockedUntilUtc <= Now)
        { user.LockedUntilUtc = null; user.FailedAccessCount = 0; }
        if (verified == PasswordVerificationResult.Failed)
        {
            user.FailedAccessCount++;
            if (user.FailedAccessCount >= settings.FailedAttempts)
            {
                user.LockedUntilUtc = Now.AddMinutes(settings.LockoutMinutes);
                await RevokeAllAsync(user, ct);
                QueueEmail(user.Email, "Bloqueo temporal de tu cuenta Ameli",
                    $"Detectamos cinco intentos fallidos. Tu acceso está bloqueado durante {settings.LockoutMinutes} minutos. Si no fuiste tú, puedes recuperar tu contraseña desde el sitio oficial.");
                var e = Audit("account_locked", "Exitoso", origin, user, detail: "Umbral de intentos alcanzado");
                e.FailedAttempts = user.FailedAccessCount; e.LockoutStartedAtUtc = Now; e.LockedUntilUtc = user.LockedUntilUtc;
            }
            throw await FailAsync(tx, "login", "invalid_login", LoginMessage, origin, user, ct, 401);
        }
        if (verified == PasswordVerificationResult.SuccessRehashNeeded)
            user.PasswordHash = passwords.HashPassword(user, request.Password);
        user.FailedAccessCount = 0;
        user.LockedUntilUtc = null;
        user.LastLoginUtc = Now;
        var session = new AuthSession
        {
            UserId = user.Id, SecurityVersion = user.SecurityVersion,
            CreatedAtUtc = Now, LastActivityAtUtc = Now,
            AbsoluteExpiresAtUtc = Now.AddHours(settings.AbsoluteSessionHours), Device = origin.Device
        };
        db.Sessions.Add(session);
        var response = RotateTokens(user, session);
        Audit("login", "Exitoso", origin, user).SessionId = session.Id;
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        return response;
    }

    public async Task ForgotPasswordAsync(ForgotPasswordRequest request, RequestOrigin origin, CancellationToken ct)
    {
        var email = NormalizeEmail(request.Email);
        var key = TokenIssuer.Hash(email);
        var userId = await db.Users.AsNoTracking().Where(x => x.NormalizedEmail == email)
            .Select(x => (Guid?)x.Id).SingleOrDefaultAsync(ct);
        var resources = new List<string> { "ameli:recovery-limits" };
        if (userId.HasValue) resources.Add(SqlSecurityTransaction.User(userId.Value));
        await using var tx = await transactions.BeginAsync(resources, ct);
        var since = Now.AddMinutes(-15);
        var accountAttempts = await db.RecoveryAttempts.CountAsync(x => x.AccountKey == key && x.CreatedAtUtc > since, ct);
        var originAttempts = await db.RecoveryAttempts.CountAsync(x => x.Origin == origin.Ip && x.CreatedAtUtc > since, ct);
        db.RecoveryAttempts.Add(new RecoveryAttempt { AccountKey = key, Origin = origin.Ip, CreatedAtUtc = Now });
        var user = userId.HasValue ? await db.Users.SingleAsync(x => x.Id == userId, ct) : null;
        if (accountAttempts >= settings.RecoveryAccountLimit || originAttempts >= settings.RecoveryOriginLimit)
            Audit("password_recovery", "Limitado", origin, user);
        else if (user is { IsActive: true } && await IsRoleActiveAsync(user.RoleName, ct))
        {
            // Requesting a newer link invalidates older links without changing the password.
            foreach (var previous in await db.PasswordResets.Where(x => x.UserId == user.Id && x.UsedAtUtc == null).ToListAsync(ct))
                previous.UsedAtUtc = Now;
            var raw = TokenIssuer.RandomToken();
            db.PasswordResets.Add(new PasswordReset
            { UserId = user.Id, TokenHash = TokenIssuer.Hash(raw), CreatedAtUtc = Now, ExpiresAtUtc = Now.AddMinutes(settings.ResetMinutes) });
            var url = $"{settings.WebBaseUrl.TrimEnd('/')}/restablecer?userId={user.Id}&token={Uri.EscapeDataString(raw)}";
            QueueEmail(user.Email, "Recupera tu acceso a Ameli", $"Abre este enlace para elegir una contraseña. Caduca en {settings.ResetMinutes} minutos y solo se puede usar una vez.\n\n{url}\n\nSi no solicitaste el cambio, ignora este correo.");
            Audit("password_recovery", "Aceptado", origin, user);
        }
        else Audit("password_recovery", "Fallido", origin, user, detail: "Recuperación no disponible");
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
    }

    public async Task ValidateResetAsync(ValidateResetRequest request, CancellationToken ct)
    {
        var hash = TokenIssuer.Hash(request.Token); var now = Now;
        var valid = await (from reset in db.PasswordResets.AsNoTracking()
            join user in db.Users on reset.UserId equals user.Id
            join role in db.Roles on user.RoleName equals role.Name
            where reset.UserId == request.UserId && reset.TokenHash == hash && reset.UsedAtUtc == null
                && reset.ExpiresAtUtc > now && user.IsActive && role.IsActive
            select reset.Id).AnyAsync(ct);
        if (!valid) throw new SecurityFault("invalid_reset", "El enlace no es válido, ya fue utilizado o expiró. Solicita uno nuevo.");
    }

    public async Task ResetPasswordAsync(ResetPasswordRequest request, RequestOrigin origin, CancellationToken ct)
    {
        await using var tx = await transactions.BeginAsync([SqlSecurityTransaction.User(request.UserId)], ct);
        var hash = TokenIssuer.Hash(request.Token);
        var reset = await db.PasswordResets.SingleOrDefaultAsync(x => x.UserId == request.UserId && x.TokenHash == hash, ct);
        var user = await db.Users.SingleOrDefaultAsync(x => x.Id == request.UserId, ct);
        if (user is not { IsActive: true } || reset is null || reset.UsedAtUtc is not null || reset.ExpiresAtUtc <= Now
            || !await IsRoleActiveAsync(user.RoleName, ct))
            throw await FailAsync(tx, "password_reset", "invalid_reset", "El enlace no es válido, ya fue utilizado o expiró. Solicita uno nuevo.", origin, user, ct);
        try { ValidateNewPassword(request.Password, request.ConfirmPassword); }
        catch (SecurityFault fault) { throw await FailAsync(tx, "password_reset", fault.Code, fault.Message, origin, user, ct); }
        user.PasswordHash = passwords.HashPassword(user, request.Password);
        user.FailedAccessCount = 0; user.LockedUntilUtc = null;
        foreach (var pending in await db.PasswordResets.Where(x => x.UserId == user.Id && x.UsedAtUtc == null).ToListAsync(ct))
            pending.UsedAtUtc = Now;
        await RevokeAllAsync(user, ct);
        QueueEmail(user.Email, "Contraseña actualizada", "Tu contraseña de Ameli fue actualizada. Todas tus sesiones anteriores se cerraron.");
        Audit("password_reset", "Exitoso", origin, user);
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
    }

    public async Task<TokenResponse> RefreshAsync(string rawToken, RequestOrigin origin, CancellationToken ct)
    {
        var hash = TokenIssuer.Hash(rawToken);
        var initial = await db.Sessions.AsNoTracking().SingleOrDefaultAsync(x => x.RefreshTokenHash == hash, ct);
        if (initial is null) throw new SecurityFault("invalid_session", "Sesión expirada. Inicia sesión nuevamente.", 401);
        await using var tx = await transactions.BeginAsync([SqlSecurityTransaction.User(initial.UserId)], ct);
        var session = await db.Sessions.SingleAsync(x => x.Id == initial.Id, ct);
        var user = await db.Users.SingleAsync(x => x.Id == session.UserId, ct);
        if (session.RefreshTokenHash != hash || !await IsValidAsync(user, session, ct))
            throw await FailAsync(tx, "session_refresh", "invalid_session", "Sesión expirada. Inicia sesión nuevamente.", origin, user, ct, 401);
        // Refreshing a token never extends the idle deadline. Only a user action does.
        var response = RotateTokens(user, session);
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        return response;
    }

    public async Task<SessionStatus?> ValidateSessionAsync(Actor actor, CancellationToken ct)
    {
        var session = await db.Sessions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == actor.SessionId && x.UserId == actor.UserId, ct);
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == actor.UserId, ct);
        return user is not null && session is not null && await IsValidAsync(user, session, ct) ? Status(user, session) : null;
    }
    public async Task<SessionStatus> TouchAsync(Actor actor, RequestOrigin origin, CancellationToken ct)
    {
        await using var tx = await transactions.BeginAsync([SqlSecurityTransaction.User(actor.UserId)], ct);
        var (user, session) = await RequireActorAsync(actor, ct);
        session.LastActivityAtUtc = Now;
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        return Status(user, session);
    }
    public async Task<List<SessionView>> SessionsAsync(Actor actor, CancellationToken ct)
    {
        var valid = await ValidateSessionAsync(actor, ct);
        if (valid is null) throw new SecurityFault("invalid_session", "Sesión expirada.", 401);
        var now = Now; var idleStart = now.AddMinutes(-settings.IdleMinutes);
        var sessions = await db.Sessions.AsNoTracking().Where(x => x.UserId == actor.UserId && x.RevokedAtUtc == null
            && x.LastActivityAtUtc > idleStart && x.AbsoluteExpiresAtUtc > now).OrderByDescending(x => x.LastActivityAtUtc).ToListAsync(ct);
        return sessions.Select(s => Session(s, s.Id == actor.SessionId)).ToList();
    }
    public async Task LogoutAsync(Actor actor, Guid? targetSession, bool all, RequestOrigin origin, CancellationToken ct)
    {
        await using var tx = await transactions.BeginAsync([SqlSecurityTransaction.User(actor.UserId)], ct);
        var (user, current) = await RequireActorAsync(actor, ct);
        if (all) await RevokeAllAsync(user, ct);
        else
        {
            var target = targetSession.HasValue ? await db.Sessions.SingleOrDefaultAsync(x => x.Id == targetSession && x.UserId == user.Id, ct) : current;
            if (target is null) throw await FailAsync(tx, "session_revoke", "not_found", "Sesión no encontrada.", origin, user, ct, 404);
            target.RevokedAtUtc ??= Now;
        }
        Audit(all ? "logout_all" : "logout", "Exitoso", origin, user);
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
    }
    public async Task ChangePasswordAsync(Actor actor, ChangePasswordRequest request, RequestOrigin origin, CancellationToken ct)
    {
        await using var tx = await transactions.BeginAsync([SqlSecurityTransaction.User(actor.UserId)], ct);
        var (user, _) = await RequireActorAsync(actor, ct);
        try { ValidateNewPassword(request.Password, request.ConfirmPassword); }
        catch (SecurityFault fault) { throw await FailAsync(tx, "password_change", fault.Code, fault.Message, origin, user, ct); }
        if (passwords.VerifyHashedPassword(user, user.PasswordHash, request.CurrentPassword) == PasswordVerificationResult.Failed)
            throw await FailAsync(tx, "password_change", "invalid_password", "No se pudo cambiar la contraseña. Revisa la contraseña actual.", origin, user, ct);
        user.PasswordHash = passwords.HashPassword(user, request.Password);
        await RevokeAllAsync(user, ct);
        foreach (var reset in await db.PasswordResets.Where(x => x.UserId == user.Id && x.UsedAtUtc == null).ToListAsync(ct)) reset.UsedAtUtc = Now;
        Audit("password_change", "Exitoso", origin, user);
        QueueEmail(user.Email, "Contraseña actualizada", "Tu contraseña fue actualizada y todas las sesiones anteriores se cerraron.");
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
    }

    public async Task<List<UserView>> UsersAsync(Actor actor, string? query, CancellationToken ct)
    {
        await using var tx = await transactions.BeginAsync([SqlSecurityTransaction.User(actor.UserId)], ct);
        var (admin, _) = await RequireActorAsync(actor, ct);
        if (admin.RoleName != Roles.Administrator) throw new SecurityFault("forbidden", "Acceso denegado.", 403);
        var users = db.Users.AsNoTracking().Where(x => x.IsInternal);
        if (!string.IsNullOrWhiteSpace(query)) users = users.Where(x => x.Name.Contains(query) || x.Email.Contains(query));
        var results = await users.OrderBy(x => x.Name).Take(200).ToListAsync(ct);
        await tx.CommitAsync(ct);
        return results.Select(User).ToList();
    }
    public Task ChangeRoleAsync(Actor actor, Guid id, ChangeRoleRequest request, RequestOrigin origin, CancellationToken ct) =>
        MutateUserAsync(actor, id, "role_change", origin, async user =>
        {
            if (!user.IsActive || !user.IsInternal || !Roles.IsInternal(request.Role) || !await IsRoleActiveAsync(request.Role, ct))
                throw new SecurityFault("invalid_role", "Selecciona un rol interno permitido para una cuenta interna activa.");
            await ProtectLastAdministratorAsync(user, request.Role, true, ct);
            var previous = user.RoleName; user.RoleName = request.Role;
            if (previous != request.Role) await RevokeAllAsync(user, ct);
            return $"Rol anterior: {previous}; rol nuevo: {user.RoleName}";
        }, ct);
    public Task ChangeStatusAsync(Actor actor, Guid id, ChangeStatusRequest request, RequestOrigin origin, CancellationToken ct) =>
        MutateUserAsync(actor, id, "account_status", origin, async user =>
        {
            await ProtectLastAdministratorAsync(user, user.RoleName, request.IsActive, ct);
            var previous = user.IsActive; user.IsActive = request.IsActive;
            if (previous != request.IsActive) await RevokeAllAsync(user, ct);
            return $"Activo anterior: {previous}; nuevo: {user.IsActive}; motivo: {request.Reason.Trim()}";
        }, ct);
    public Task UnlockAsync(Actor actor, Guid id, UnlockRequest request, RequestOrigin origin, CancellationToken ct) =>
        MutateUserAsync(actor, id, "administrative_unlock", origin, async user =>
        {
            if (user.LockedUntilUtc is null || user.LockedUntilUtc <= Now)
                throw new SecurityFault("not_locked", "La cuenta no tiene un bloqueo temporal vigente.");
            user.LockedUntilUtc = null; user.FailedAccessCount = 0;
            await RevokeAllAsync(user, ct);
            return $"Motivo: {request.Reason.Trim()}; referencia de validación de identidad: {request.IdentityEvidence.Trim()}";
        }, ct);

    public Task RevokeUserSessionsAsync(Actor actor, Guid id, RevokeUserSessionsRequest request, RequestOrigin origin, CancellationToken ct) =>
        MutateUserAsync(actor, id, "administrative_revoke_sessions", origin, async user =>
        {
            await RevokeAllAsync(user, ct);
            return $"Cierre global de sesiones; motivo: {request.Reason.Trim()}";
        }, ct);

    private async Task MutateUserAsync(Actor actor, Guid id, string action, RequestOrigin origin, Func<AppUser, Task<string>> change, CancellationToken ct)
    {
        await using var tx = await transactions.BeginAsync(["ameli:administrators", SqlSecurityTransaction.User(actor.UserId), SqlSecurityTransaction.User(id)], ct);
        var (admin, currentSession) = await RequireActorAsync(actor, ct);
        if (admin.RoleName != Roles.Administrator) throw new SecurityFault("forbidden", "Acceso denegado.", 403);
        var actorRole = admin.RoleName;
        var target = await db.Users.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (target is null) throw await FailAsync(tx, action, "not_found", "Cuenta no encontrada.", origin, admin, ct, 404);
        try
        {
            var before = Snapshot(target);
            var detail = await change(target);
            target.Revision = Guid.NewGuid(); target.UpdatedAtUtc = Now;
            currentSession.LastActivityAtUtc = Now;
            var e = Audit(action, "Exitoso", origin, admin, target.Id, detail, actorRole);
            e.BeforeJson = before; e.AfterJson = Snapshot(target); e.SessionId = actor.SessionId;
        }
        catch (SecurityFault fault)
        {
            Audit(action, "Rechazado", origin, admin, target.Id, fault.Code);
            await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
            throw;
        }
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
    }
    private async Task ProtectLastAdministratorAsync(AppUser user, string newRole, bool active, CancellationToken ct)
    {
        if (user.IsActive && user.RoleName == Roles.Administrator && (!active || newRole != Roles.Administrator)
            && !await db.Users.AnyAsync(x => x.Id != user.Id && x.IsActive && x.RoleName == Roles.Administrator, ct))
            throw new SecurityFault("last_administrator", "No puedes quitar el acceso al último administrador activo. Habilita otro administrador primero.", 409);
    }
    private async Task<(AppUser User, AuthSession Session)> RequireActorAsync(Actor actor, CancellationToken ct)
    {
        var user = await db.Users.SingleOrDefaultAsync(x => x.Id == actor.UserId, ct);
        var session = await db.Sessions.SingleOrDefaultAsync(x => x.Id == actor.SessionId && x.UserId == actor.UserId, ct);
        if (user is null || session is null || !await IsValidAsync(user, session, ct))
            throw new SecurityFault("invalid_session", "Sesión expirada. Inicia sesión nuevamente.", 401);
        return (user, session);
    }
    private async Task<bool> IsValidAsync(AppUser user, AuthSession session, CancellationToken ct) =>
        user.IsActive && !(user.LockedUntilUtc > Now) && session.RevokedAtUtc is null
        && session.SecurityVersion == user.SecurityVersion && session.AbsoluteExpiresAtUtc > Now
        && session.LastActivityAtUtc.AddMinutes(settings.IdleMinutes) > Now && await IsRoleActiveAsync(user.RoleName, ct);
    private Task<bool> IsRoleActiveAsync(string role, CancellationToken ct) => db.Roles.AnyAsync(x => x.Name == role && x.IsActive, ct);
    private async Task RevokeAllAsync(AppUser user, CancellationToken ct)
    {
        user.SecurityVersion++;
        foreach (var session in await db.Sessions.Where(x => x.UserId == user.Id && x.RevokedAtUtc == null).ToListAsync(ct)) session.RevokedAtUtc = Now;
    }
    private TokenResponse RotateTokens(AppUser user, AuthSession session)
    {
        var raw = TokenIssuer.RandomToken(); session.RefreshTokenHash = TokenIssuer.Hash(raw);
        var (jwt, expires) = tokens.Issue(user, session);
        return new TokenResponse(jwt, raw, expires, Status(user, session));
    }
    private SessionStatus Status(AppUser user, AuthSession session) => new(User(user), Session(session, true));
    private SessionView Session(AuthSession s, bool current) => new(s.Id, s.CreatedAtUtc, s.LastActivityAtUtc,
        s.LastActivityAtUtc.AddMinutes(settings.IdleMinutes), s.AbsoluteExpiresAtUtc, s.Device, current);
    private static UserView User(AppUser u) => new(u.Id, u.Name, u.Email, u.RoleName, u.IsActive, u.LockedUntilUtc, u.LastLoginUtc);
    private static void ValidateNewPassword(string password, string confirmation)
    {
        if (password != confirmation || !IsStrongPassword(password))
            throw new SecurityFault("password_policy", "Usa de 8 a 64 caracteres, mayúscula, minúscula, número y símbolo. Las contraseñas deben coincidir.");
    }
    private void QueueEmail(string recipient, string subject, string body) => db.Emails.Add(new OutgoingEmail
    { Recipient = recipient, Subject = subject, ProtectedBody = emailProtector.Protect(body), CreatedAtUtc = Now, NextAttemptAtUtc = Now });
    private SecurityEvent Audit(string action, string outcome, RequestOrigin origin, AppUser? actor = null, Guid? subjectId = null, string detail = "", string? actorRole = null)
    {
        var e = new SecurityEvent { OccurredAtUtc = Now, ActorUserId = actor?.Id, SubjectUserId = subjectId ?? actor?.Id,
            ActorName = actor?.Name ?? "", ActorEmail = actor?.Email ?? "", ActorRole = actorRole ?? actor?.RoleName ?? "",
            Action = action, Outcome = outcome, Origin = origin.Ip, CorrelationId = origin.CorrelationId, Detail = detail,
            EntityId = (subjectId ?? actor?.Id)?.ToString() ?? "" };
        db.Events.Add(e); return e;
    }
    private async Task<SecurityFault> FailAsync(IDbContextTransaction tx, string action, string code, string message,
        RequestOrigin origin, AppUser? user, CancellationToken ct, int status = 400)
    {
        Audit(action, action == "login" ? "Fallido" : "Rechazado", origin, user, detail: action == "login" ? "Credenciales no aceptadas" : code);
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        return new SecurityFault(code, message, status);
    }
}
