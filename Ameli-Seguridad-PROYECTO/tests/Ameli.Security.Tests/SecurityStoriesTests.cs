using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using Ameli.Api.Application;
using Ameli.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Xunit;
using static Ameli.Security.Tests.SecurityFixture;

namespace Ameli.Security.Tests;

public sealed class SecurityStoriesTests(SecurityFixture f) : IClassFixture<SecurityFixture>, IAsyncLifetime
{
    public Task InitializeAsync() => f.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Theory]
    [InlineData("cliente@ameli.test", Roles.Client)]
    [InlineData("admin@ameli.test", Roles.Administrator)]
    [InlineData("logistica@ameli.test", Roles.Logistics)]
    public async Task SYA001_Login_EmitsValidJwtForEachRole(string email, string role)
    {
        using var http = f.Http();
        var response = await http.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest { Email = email, Password = Password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var token = (await response.Content.ReadFromJsonAsync<TokenResponse>())!;
        Assert.Equal(role, token.Status.User.Role);
        using var authenticated = f.Http(token);
        Assert.Equal(HttpStatusCode.OK, (await authenticated.GetAsync("/api/v1/auth/session")).StatusCode);
        var hash = await f.Db(db => db.Users.Where(u => u.Id == token.Status.User.Id).Select(u => u.PasswordHash).SingleAsync());
        Assert.NotEqual(Password, hash);
    }

    [Fact]
    public async Task SYA001_UnknownWrongAndInactiveHaveSameResponse()
    {
        await f.AddUser("inactive@ameli.test", Roles.Client, false);
        var wrong = await Assert.ThrowsAsync<SecurityFault>(() => f.Login(password: "incorrecta"));
        var unknown = await Assert.ThrowsAsync<SecurityFault>(() => f.Login("missing@ameli.test"));
        var inactive = await Assert.ThrowsAsync<SecurityFault>(() => f.Login("inactive@ameli.test"));
        Assert.Equal(wrong.Message, unknown.Message); Assert.Equal(wrong.Message, inactive.Message);
        Assert.Equal(401, inactive.Status);
        Assert.Equal(3, await f.Db(db => db.Events.CountAsync(e => e.Action == "login")));
    }

    [Fact]
    public async Task SYA001_EmptyFieldsAreRejectedByApi()
    {
        using var http = f.Http();
        Assert.Equal(HttpStatusCode.BadRequest, (await http.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest())).StatusCode);
        Assert.Equal(0, await f.Db(db => db.Sessions.CountAsync()));
    }

    [Fact]
    public async Task SYA005_FiveConcurrentFailuresLockWithoutLostUpdates()
    {
        await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => Assert.ThrowsAsync<SecurityFault>(() => f.Login(password: "incorrecta"))));
        var user = await f.Db(db => db.Users.AsNoTracking().SingleAsync(u => u.Id == f.Client));
        Assert.Equal(5, user.FailedAccessCount);
        Assert.Equal(f.Clock.GetUtcNow().AddMinutes(15), user.LockedUntilUtc);
        await Assert.ThrowsAsync<SecurityFault>(() => f.Login());
        Assert.Equal(1, await f.Db(db => db.Emails.CountAsync(e => e.Subject.StartsWith("Bloqueo"))));
        f.Clock.Advance(TimeSpan.FromMinutes(15));
        await f.Login();
        Assert.Equal(0, await f.Db(db => db.Users.Where(u => u.Id == f.Client).Select(u => u.FailedAccessCount).SingleAsync()));
    }

    [Fact]
    public async Task SYA005_SuccessResetsConsecutiveFailureCounter()
    {
        for (var i = 0; i < 4; i++) await Assert.ThrowsAsync<SecurityFault>(() => f.Login(password: "incorrecta"));
        await f.Login();
        await Assert.ThrowsAsync<SecurityFault>(() => f.Login(password: "incorrecta"));
        var user = await f.Db(db => db.Users.SingleAsync(u => u.Id == f.Client));
        Assert.Equal(1, user.FailedAccessCount); Assert.Null(user.LockedUntilUtc);
    }

    [Fact]
    public async Task SYA005_AdminUnlockRequiresEvidenceAndIsAudited()
    {
        var admin = await f.Login("admin@ameli.test");
        for (var i = 0; i < 5; i++) await Assert.ThrowsAsync<SecurityFault>(() => f.Login(password: "incorrecta"));
        using var http = f.Http(admin);
        var path = $"/api/v1/admin/users/{f.Client}/unlock";
        Assert.Equal(HttpStatusCode.BadRequest, (await http.PostAsJsonAsync(path, new UnlockRequest { Reason = "Soporte verificado" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await http.PostAsJsonAsync(path, new UnlockRequest { Reason = "Solicitud de soporte", IdentityEvidence = "Validación presencial, caso TEST-001" })).StatusCode);
        await f.Login();
        var audit = await f.Db(db => db.Events.SingleAsync(e => e.Action == "administrative_unlock"));
        Assert.Equal(f.Admin, audit.ActorUserId); Assert.Contains("TEST-001", audit.Detail);
    }

    [Fact]
    public async Task SYA002_RecoveryDoesNotDiscloseExistence()
    {
        using var http = f.Http();
        var known = await http.PostAsJsonAsync("/api/v1/auth/forgot-password", new { email = "cliente@ameli.test" });
        var unknown = await http.PostAsJsonAsync("/api/v1/auth/forgot-password", new { email = "missing@ameli.test" });
        Assert.Equal(HttpStatusCode.Accepted, known.StatusCode); Assert.Equal(known.StatusCode, unknown.StatusCode);
        Assert.Equal(await known.Content.ReadAsStringAsync(), await unknown.Content.ReadAsStringAsync());
        Assert.Equal(1, await f.Db(db => db.PasswordResets.CountAsync()));
    }

    [Fact]
    public async Task SYA002_LinkIsSingleUseAndRevokesExistingTokens()
    {
        var session = await f.Login(); var token = await f.RequestReset();
        var stored = await f.Db(db => db.PasswordResets.SingleAsync()); Assert.NotEqual(token, stored.TokenHash);
        var weak = await Assert.ThrowsAsync<SecurityFault>(() => f.ResetPassword(token, "abcdefgh"));
        Assert.Equal("password_policy", weak.Code);
        var outcomes = await Task.WhenAll(Enumerable.Range(0, 2).Select(async _ =>
        { try { await f.ResetPassword(token); return true; } catch (SecurityFault) { return false; } }));
        Assert.Single(outcomes, x => x);
        await Assert.ThrowsAsync<SecurityFault>(() => f.WithService(s => s.ValidateResetAsync(new() { UserId = f.Client, Token = token }, default)));
        using var old = f.Http(session);
        Assert.Equal(HttpStatusCode.Unauthorized, (await old.GetAsync("/api/v1/auth/session")).StatusCode);
        await Assert.ThrowsAsync<SecurityFault>(() => f.WithService(s => s.RefreshAsync(session.RefreshToken, Origin, default)));
        await Assert.ThrowsAsync<SecurityFault>(() => f.Login());
        await f.Login(password: NewPassword);
        var details = await f.Db(db => db.Events.Select(e => e.Detail).ToListAsync());
        Assert.All(details, detail => { Assert.DoesNotContain(token, detail); Assert.DoesNotContain(NewPassword, detail); });
    }

    [Fact]
    public async Task SYA002_ResetExpiresAtFifteenMinutes()
    {
        var token = await f.RequestReset();
        await f.WithService(s => s.ValidateResetAsync(new() { UserId = f.Client, Token = token }, default));
        f.Clock.Advance(TimeSpan.FromMinutes(15));
        await Assert.ThrowsAsync<SecurityFault>(() => f.WithService(s => s.ValidateResetAsync(new() { UserId = f.Client, Token = token }, default)));
        Assert.Equal("invalid_reset", (await Assert.ThrowsAsync<SecurityFault>(() => f.ResetPassword(token))).Code);
    }

    [Fact]
    public async Task SYA002_AccountAndOriginLimitsDoNotSendExtraLinks()
    {
        for (var i = 0; i < 4; i++) await f.WithService(s => s.ForgotPasswordAsync(new() { Email = "cliente@ameli.test" }, Origin, default));
        Assert.Equal(3, await f.Db(db => db.Emails.CountAsync()));
        for (var i = 0; i < 6; i++) await f.WithService(s => s.ForgotPasswordAsync(new() { Email = $"unknown{i}@ameli.test" }, Origin, default));
        await f.WithService(s => s.ForgotPasswordAsync(new() { Email = "admin@ameli.test" }, Origin, default));
        Assert.Equal(3, await f.Db(db => db.Emails.CountAsync()));
        Assert.Equal(2, await f.Db(db => db.Events.CountAsync(e => e.Outcome == "Limitado")));
    }

    [Fact]
    public async Task SYA003_RoleChangeIsAuditedAndImmediatelyRevokesOldJwt()
    {
        var admin = await f.Login("admin@ameli.test"); var staff = await f.Login("logistica@ameli.test");
        await f.WithService(s => s.ChangeRoleAsync(Actor(admin), f.Logistics, new() { Role = Roles.Administrator }, Origin, default));
        using var old = f.Http(staff);
        Assert.Equal(HttpStatusCode.Unauthorized, (await old.GetAsync("/api/v1/admin/users")).StatusCode);
        Assert.Equal(Roles.Administrator, (await f.Login("logistica@ameli.test")).Status.User.Role);
        var audit = await f.Db(db => db.Events.SingleAsync(e => e.Action == "role_change"));
        Assert.Equal(f.Admin, audit.ActorUserId); Assert.Equal(f.Logistics, audit.SubjectUserId);
        Assert.Contains(Roles.Logistics, audit.Detail); Assert.Contains(Roles.Administrator, audit.Detail);
    }

    [Theory]
    [InlineData("cliente@ameli.test")]
    [InlineData("logistica@ameli.test")]
    public async Task SYA003_NonAdminCannotReadOrWriteAdminApi(string email)
    {
        using var http = f.Http(await f.Login(email));
        Assert.Equal(HttpStatusCode.Forbidden, (await http.GetAsync("/api/v1/admin/users")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await http.PutAsJsonAsync($"/api/v1/admin/users/{f.Admin}/role", new { role = Roles.Logistics })).StatusCode);
        Assert.Equal(Roles.Administrator, await f.Db(db => db.Users.Where(u => u.Id == f.Admin).Select(u => u.RoleName).SingleAsync()));
    }

    [Fact]
    public async Task SYA003_RejectsUnknownInactiveAndIncompatibleRoles()
    {
        var admin = Actor(await f.Login("admin@ameli.test"));
        foreach (var role in new[] { "SuperAdmin", Roles.Client })
            Assert.Equal("invalid_role", (await Assert.ThrowsAsync<SecurityFault>(() => f.WithService(s => s.ChangeRoleAsync(admin, f.Logistics, new() { Role = role }, Origin, default)))).Code);
        await Assert.ThrowsAsync<SecurityFault>(() => f.WithService(s => s.ChangeRoleAsync(admin, f.Client, new() { Role = Roles.Administrator }, Origin, default)));
        await f.Db(async db => { await db.Roles.Where(r => r.Name == Roles.Logistics).ExecuteUpdateAsync(s => s.SetProperty(r => r.IsActive, false)); });
        await Assert.ThrowsAsync<SecurityFault>(() => f.WithService(s => s.ChangeRoleAsync(admin, f.Admin, new() { Role = Roles.Logistics }, Origin, default)));
    }

    [Fact]
    public async Task SYA003_LastAdministratorCannotBeDemotedOrDisabled()
    {
        var admin = Actor(await f.Login("admin@ameli.test"));
        var role = await Assert.ThrowsAsync<SecurityFault>(() => f.WithService(s => s.ChangeRoleAsync(admin, f.Admin, new() { Role = Roles.Logistics }, Origin, default)));
        var status = await Assert.ThrowsAsync<SecurityFault>(() => f.WithService(s => s.ChangeStatusAsync(admin, f.Admin, new() { IsActive = false, Reason = "Prueba controlada" }, Origin, default)));
        Assert.Equal(409, role.Status); Assert.Equal(409, status.Status);
    }

    [Fact]
    public async Task SYA003_ConcurrentSelfDemotionsKeepOneAdministrator()
    {
        var second = await f.AddUser("second@ameli.test", Roles.Administrator);
        var firstToken = await f.Login("admin@ameli.test"); var secondToken = await f.Login("second@ameli.test");
        var outcomes = await Task.WhenAll(new[] { firstToken, secondToken }.Select(async token =>
        { try { await f.WithService(s => s.ChangeRoleAsync(Actor(token), token.Status.User.Id, new() { Role = Roles.Logistics }, Origin, default)); return true; } catch (SecurityFault fault) { Assert.Equal(409, fault.Status); return false; } }));
        Assert.Single(outcomes, x => x);
        Assert.Equal(1, await f.Db(db => db.Users.CountAsync(u => u.IsActive && u.RoleName == Roles.Administrator)));
        var audit = await f.Db(db => db.Events.SingleAsync(e => e.Action == "role_change" && e.Outcome == "Exitoso"));
        Assert.Equal(Roles.Administrator, audit.ActorRole);
    }

    [Fact]
    public async Task SYA006_ActivityRenewsIdleDeadlineButPollingAndRefreshDoNot()
    {
        var token = await f.Login(); var actor = Actor(token);
        f.Clock.Advance(TimeSpan.FromMinutes(14));
        await f.WithService(s => s.TouchAsync(actor, Origin, default));
        f.Clock.Advance(TimeSpan.FromMinutes(14));
        Assert.NotNull(await f.WithService(s => s.ValidateSessionAsync(actor, default)));
        var rotated = await f.WithService(s => s.RefreshAsync(token.RefreshToken, Origin, default));
        Assert.NotEqual(token.RefreshToken, rotated.RefreshToken);
        await Assert.ThrowsAsync<SecurityFault>(() => f.WithService(s => s.RefreshAsync(token.RefreshToken, Origin, default)));
        f.Clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Null(await f.WithService(s => s.ValidateSessionAsync(actor, default)));
        await Assert.ThrowsAsync<SecurityFault>(() => f.WithService(s => s.RefreshAsync(rotated.RefreshToken, Origin, default)));
        await Assert.ThrowsAsync<SecurityFault>(() => f.WithService(s => s.TouchAsync(actor, Origin, default)));
    }

    [Fact]
    public async Task SYA006_IdleJwtIsRejectedBeforeSideEffects()
    {
        var token = await f.Login("admin@ameli.test"); f.Clock.Advance(TimeSpan.FromMinutes(15));
        using var http = f.Http(token);
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.PutAsJsonAsync($"/api/v1/admin/users/{f.Client}/status", new { isActive = false, reason = "Solicitud de pruebas" })).StatusCode);
        Assert.True(await f.Db(db => db.Users.Where(u => u.Id == f.Client).Select(u => u.IsActive).SingleAsync()));
    }

    [Fact]
    public async Task SYA006_ExpiredAndTamperedJwtAreRejected()
    {
        var token = await f.Login(); var parsed = new JwtSecurityTokenHandler().ReadJwtToken(token.AccessToken);
        var expired = new JwtSecurityToken("Ameli.Security", "Ameli.Clients", parsed.Claims.Where(c => c.Type is "sub" or "sid" or "role"),
            DateTime.UtcNow.AddMinutes(-10), DateTime.UtcNow.AddMinutes(-1),
            new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(f.SigningKey)), SecurityAlgorithms.HmacSha256));
        using var http = f.Http();
        foreach (var jwt in new[] { new JwtSecurityTokenHandler().WriteToken(expired), token.AccessToken[..^8] + "invalid!" })
        {
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
            Assert.Equal(HttpStatusCode.Unauthorized, (await http.GetAsync("/api/v1/auth/session")).StatusCode);
        }
    }

    [Fact]
    public async Task SYA006_PasswordChangeLogoutAndDeactivationRevokeSessions()
    {
        var admin = Actor(await f.Login("admin@ameli.test"));
        var first = await f.Login(); var second = await f.Login();
        await f.WithService(s => s.ChangePasswordAsync(Actor(first), new() { CurrentPassword = Password, Password = NewPassword, ConfirmPassword = NewPassword }, Origin, default));
        Assert.Null(await f.WithService(s => s.ValidateSessionAsync(Actor(second), default)));
        first = await f.Login(password: NewPassword); second = await f.Login(password: NewPassword);
        await f.WithService(s => s.LogoutAsync(Actor(first), null, true, Origin, default));
        Assert.Null(await f.WithService(s => s.ValidateSessionAsync(Actor(second), default)));
        first = await f.Login(password: NewPassword);
        await f.WithService(s => s.ChangeStatusAsync(admin, f.Client, new() { IsActive = false, Reason = "Solicitud verificada" }, Origin, default));
        using var http = f.Http(first);
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.GetAsync("/api/v1/auth/session")).StatusCode);
    }

    [Fact]
    public async Task SYA006_AdministratorCanRevokeAllSessionsWithReason()
    {
        using var admin = f.Http(await f.Login("admin@ameli.test"));
        var first = await f.Login(); var second = await f.Login();
        var path = $"/api/v1/admin/users/{f.Client}/revoke-sessions";
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync(path, new { reason = "" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsJsonAsync(path, new { reason = "Incidente verificado por soporte" })).StatusCode);
        using var old = f.Http(first);
        Assert.Equal(HttpStatusCode.Unauthorized, (await old.GetAsync("/api/v1/auth/session")).StatusCode);
        Assert.Null(await f.WithService(s => s.ValidateSessionAsync(Actor(second), default)));
        var audit = await f.Db(db => db.Events.SingleAsync(e => e.Action == "administrative_revoke_sessions"));
        Assert.Equal(f.Admin, audit.ActorUserId); Assert.Equal(f.Client, audit.SubjectUserId);
    }

    [Fact]
    public async Task SYA006_SessionRevocationCannotTargetAnotherUser()
    {
        var client = await f.Login(); var other = await f.Login("logistica@ameli.test");
        var error = await Assert.ThrowsAsync<SecurityFault>(() => f.WithService(s => s.LogoutAsync(Actor(client), other.Status.Session.Id, false, Origin, default)));
        Assert.Equal(404, error.Status);
        Assert.NotNull(await f.WithService(s => s.ValidateSessionAsync(Actor(other), default)));
        await f.WithService(s => s.LogoutAsync(Actor(client), null, false, Origin, default));
        using var http = f.Http(client);
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.GetAsync("/api/v1/auth/session")).StatusCode);
    }
}
