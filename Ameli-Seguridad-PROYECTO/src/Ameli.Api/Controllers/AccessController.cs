using System.Security.Claims;
using Ameli.Api.Application;
using Ameli.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ameli.Api.Controllers;

[ApiController, Route("api/v1/admin/users"), Authorize(Roles = Roles.Administrator)]
public sealed class AccessController(SecurityService security) : ControllerBase
{
    private Actor Actor => new(Guid.Parse(User.FindFirstValue("sub")!), Guid.Parse(User.FindFirstValue("sid")!));
    [HttpGet]
    public Task<List<UserView>> List([FromQuery] string? q, CancellationToken ct) => security.UsersAsync(Actor, q is { Length: > 100 } ? q[..100] : q, ct);
    [HttpPut("{id:guid}/role")]
    public async Task<IActionResult> Role(Guid id, ChangeRoleRequest request, CancellationToken ct)
    { await security.ChangeRoleAsync(Actor, id, request, HttpOrigin.Create(HttpContext), ct); return NoContent(); }
    [HttpPut("{id:guid}/status")]
    public async Task<IActionResult> Status(Guid id, ChangeStatusRequest request, CancellationToken ct)
    { await security.ChangeStatusAsync(Actor, id, request, HttpOrigin.Create(HttpContext), ct); return NoContent(); }
    [HttpPost("{id:guid}/unlock")]
    public async Task<IActionResult> Unlock(Guid id, UnlockRequest request, CancellationToken ct)
    { await security.UnlockAsync(Actor, id, request, HttpOrigin.Create(HttpContext), ct); return NoContent(); }
    [HttpPost("{id:guid}/revoke-sessions")]
    public async Task<IActionResult> RevokeSessions(Guid id, RevokeUserSessionsRequest request, CancellationToken ct)
    { await security.RevokeUserSessionsAsync(Actor, id, request, HttpOrigin.Create(HttpContext), ct); return NoContent(); }
}
