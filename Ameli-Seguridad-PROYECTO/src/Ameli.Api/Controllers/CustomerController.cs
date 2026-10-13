using System.Security.Claims;
using Ameli.Api.Application;
using Ameli.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ameli.Api.Controllers;

[ApiController, Route("api/v1/customer"), Authorize(Roles = Roles.Client)]
public sealed class CustomerController(CustomerIdentityService service) : ControllerBase
{
    private Actor Actor => new(Guid.Parse(User.FindFirstValue("sub")!), Guid.Parse(User.FindFirstValue("sid")!));
    private RequestOrigin Origin => HttpOrigin.Create(HttpContext);

    private async Task<ActionResult<T>> Respond<T>(Func<Task<T>> operation)
    {
        try { return Ok(await operation()); }
        catch (SecurityFault fault) { return StatusCode(fault.Status, fault.ToError()); }
    }

    [HttpGet("pets")]
    public Task<ActionResult<List<PetView>>> Pets(CancellationToken ct) => Respond(() => service.PetsAsync(Actor, ct));

    [HttpGet("pets/{id:guid}")]
    public Task<ActionResult<PetView>> Pet(Guid id, CancellationToken ct) => Respond(() => service.PetAsync(Actor, id, ct));

    [HttpPost("pets")]
    public Task<ActionResult<PetView>> CreatePet(PetRequest request, CancellationToken ct) =>
        Respond(() => service.CreatePetAsync(Actor, request, Origin, ct));

    [HttpPut("pets/{id:guid}")]
    public Task<ActionResult<PetChangeResult>> EditPet(Guid id, PetRequest request, CancellationToken ct) =>
        Respond(() => service.UpdatePetAsync(Actor, id, request, Origin, ct));

    [HttpPost("pets/{id:guid}/deactivate")]
    public Task<ActionResult<PetChangeResult>> Deactivate(Guid id, CancellationToken ct) =>
        Respond(() => service.SetPetActiveAsync(Actor, id, false, Origin, ct));

    [HttpPost("pets/{id:guid}/reactivate")]
    public Task<ActionResult<PetChangeResult>> Reactivate(Guid id, CancellationToken ct) =>
        Respond(() => service.SetPetActiveAsync(Actor, id, true, Origin, ct));

    [HttpPost("pets/{id:guid}/photo")]
    public Task<ActionResult<PetChangeResult>> SavePhoto(Guid id, PetPhotoRequest request, CancellationToken ct) =>
        Respond(() => service.SavePhotoAsync(Actor, id, request, Origin, ct));

    [HttpGet("pets/{id:guid}/photo")]
    public Task<ActionResult<PetPhotoPayload>> Photo(Guid id, CancellationToken ct) =>
        Respond(() => service.PhotoAsync(Actor, id, ct));

    [HttpGet("pets/{id:guid}/preferences")]
    public Task<ActionResult<PetPreferencesView>> Preferences(Guid id, CancellationToken ct) =>
        Respond(() => service.PreferencesAsync(Actor, id, ct));

    [HttpPut("pets/{id:guid}/preferences")]
    public Task<ActionResult<PetPreferencesView>> SavePreferences(Guid id, PetPreferencesRequest request, CancellationToken ct) =>
        Respond(() => service.SavePreferencesAsync(Actor, id, request, Origin, ct));

    [HttpGet("addresses")]
    public Task<ActionResult<List<DeliveryAddressView>>> Addresses(CancellationToken ct) =>
        Respond(() => service.AddressesAsync(Actor, ct));

    [HttpPost("addresses")]
    public Task<ActionResult<DeliveryAddressView>> AddAddress(DeliveryAddressRequest request, CancellationToken ct) =>
        Respond(() => service.AddAddressAsync(Actor, request, Origin, ct));

    [HttpPost("addresses/{id:guid}/default")]
    public Task<ActionResult<DeliveryAddressView>> SetDefault(Guid id, CancellationToken ct) =>
        Respond(() => service.SetDefaultAddressAsync(Actor, id, Origin, ct));

    [HttpDelete("addresses/{id:guid}")]
    public async Task<IActionResult> DeleteAddress(Guid id, CancellationToken ct)
    {
        try { await service.DeleteAddressAsync(Actor, id, Origin, ct); return NoContent(); }
        catch (SecurityFault fault) { return StatusCode(fault.Status, fault.ToError()); }
    }
}
