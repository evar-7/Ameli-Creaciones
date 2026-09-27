using System.Security.Claims;
using Ameli.Api.Application;
using Ameli.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
namespace Ameli.Api.Controllers;
[ApiController,Route("api/v1/admin/audit"),Authorize(Roles=Roles.Administrator)]
public sealed class AuditController(SecurityService service):ControllerBase
{
    private Actor Actor=>new(Guid.Parse(User.FindFirstValue("sub")!),Guid.Parse(User.FindFirstValue("sid")!));
    [HttpGet] public Task<PagedResult<AuditEventView>> List([FromQuery]AuditQuery q,CancellationToken ct)=>service.AuditEventsAsync(Actor,q,ct);
    [HttpPost("verify"),EnableRateLimiting("auth")] public Task<IntegrityReport> Verify(AuditQuery q,CancellationToken ct)=>service.VerifyAuditAsync(Actor,q,HttpOrigin.Create(HttpContext),ct);
    [HttpPost("export"),EnableRateLimiting("auth")] public async Task<IActionResult> Export(AuditExportRequest q,CancellationToken ct)=>File(await service.ExportAuditAsync(Actor,q,HttpOrigin.Create(HttpContext),ct),"application/zip",$"Ameli-Auditoria-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.zip");
}
