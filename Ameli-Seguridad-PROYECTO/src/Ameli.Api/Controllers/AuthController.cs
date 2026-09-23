using System.Security.Claims;
using Ameli.Api.Application;
using Ameli.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Ameli.Api.Controllers;

[ApiController, Route("api/v1/auth")]
public sealed class AuthController(SecurityService security) : ControllerBase
{
    private Actor Actor => new(Guid.Parse(User.FindFirstValue("sub")!), Guid.Parse(User.FindFirstValue("sid")!));
    private RequestOrigin Origin => HttpOrigin.Create(HttpContext);

    [HttpPost("login"), AllowAnonymous, EnableRateLimiting("auth")]
    public Task<TokenResponse> Login(LoginRequest request, CancellationToken ct) => security.LoginAsync(request, Origin, ct);
    [HttpPost("forgot-password"), AllowAnonymous, EnableRateLimiting("recovery")]
    public async Task<IActionResult> Forgot(ForgotPasswordRequest request, CancellationToken ct)
    { await security.ForgotPasswordAsync(request, Origin, ct); return Accepted(new ApiMessage(SecurityService.RecoveryMessage)); }
    [HttpPost("reset-password"), AllowAnonymous, EnableRateLimiting("auth")]
    public async Task<ApiMessage> Reset(ResetPasswordRequest request, CancellationToken ct)
    { await security.ResetPasswordAsync(request, Origin, ct); return new("Contraseña actualizada. Inicia sesión nuevamente."); }
    [HttpPost("validate-reset"), AllowAnonymous, EnableRateLimiting("auth")]
    public async Task<ApiMessage> ValidateReset(ValidateResetRequest request, CancellationToken ct)
    { await security.ValidateResetAsync(request, ct); return new("Enlace vigente."); }
    [HttpPost("refresh"), AllowAnonymous, EnableRateLimiting("auth")]
    public Task<TokenResponse> Refresh(RefreshRequest request, CancellationToken ct) => security.RefreshAsync(request.RefreshToken, Origin, ct);
    [HttpGet("session"), Authorize]
    public async Task<ActionResult<SessionStatus>> Session(CancellationToken ct)
    { var result = await security.ValidateSessionAsync(Actor, ct); return result is null ? Unauthorized() : Ok(result); }
    [HttpPost("activity"), Authorize]
    public Task<SessionStatus> Activity(CancellationToken ct) => security.TouchAsync(Actor, Origin, ct);
    [HttpGet("sessions"), Authorize]
    public Task<List<SessionView>> Sessions(CancellationToken ct) => security.SessionsAsync(Actor, ct);
    [HttpPost("logout"), Authorize]
    public async Task<IActionResult> Logout(CancellationToken ct)
    { await security.LogoutAsync(Actor, null, false, Origin, ct); return NoContent(); }
    [HttpPost("logout-all"), Authorize]
    public async Task<IActionResult> LogoutAll(CancellationToken ct)
    { await security.LogoutAsync(Actor, null, true, Origin, ct); return NoContent(); }
    [HttpDelete("sessions/{id:guid}"), Authorize]
    public async Task<IActionResult> Revoke(Guid id, CancellationToken ct)
    { await security.LogoutAsync(Actor, id, false, Origin, ct); return NoContent(); }
    [HttpPost("change-password"), Authorize, EnableRateLimiting("auth")]
    public async Task<ApiMessage> ChangePassword(ChangePasswordRequest request, CancellationToken ct)
    { await security.ChangePasswordAsync(Actor, request, Origin, ct); return new("Contraseña actualizada. Inicia sesión nuevamente."); }
}
public static class HttpOrigin
{
    public static RequestOrigin Create(HttpContext context) => new(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        Limit(context.Request.Headers.UserAgent.ToString(), 240), Limit(context.TraceIdentifier, 100));
    private static string Limit(string value, int max) => value.Length > max ? value[..max] : value;
}
