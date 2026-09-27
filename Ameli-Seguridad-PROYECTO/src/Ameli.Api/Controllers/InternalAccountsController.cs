using System.Security.Claims;
using Ameli.Api.Application;
using Ameli.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace Ameli.Api.Controllers;
[ApiController,Route("api/v1/admin/internal-accounts"),Authorize(Roles=Roles.Administrator)]
public sealed class InternalAccountsController(SecurityService service):ControllerBase
{
    private Actor Actor=>new(Guid.Parse(User.FindFirstValue("sub")!),Guid.Parse(User.FindFirstValue("sid")!));
    [HttpGet] public Task<PagedResult<InternalAccountView>> List([FromQuery]InternalAccountQuery q,CancellationToken ct)=>service.InternalAccountsAsync(Actor,q,ct);
    [HttpGet("{id:guid}")] public Task<InternalAccountView> Get(Guid id,CancellationToken ct)=>service.InternalAccountAsync(Actor,id,ct);
    [HttpPost] public async Task<IActionResult> Create(CreateInternalAccountRequest r,CancellationToken ct){var u=await service.CreateInternalAsync(Actor,r,HttpOrigin.Create(HttpContext),ct);return CreatedAtAction(nameof(Get),new{id=u.Id},u);}
    [HttpPut("{id:guid}")] public Task<InternalAccountView> Update(Guid id,UpdateInternalAccountRequest r,CancellationToken ct)=>service.UpdateInternalAsync(Actor,id,r,HttpOrigin.Create(HttpContext),ct);
    [HttpPut("{id:guid}/state")] public Task<InternalAccountView> State(Guid id,InternalStateRequest r,CancellationToken ct)=>service.SetInternalStateAsync(Actor,id,r,HttpOrigin.Create(HttpContext),ct);
}
