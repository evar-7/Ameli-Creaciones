using System.Security.Claims;
using Ameli.Api.Application;
using Ameli.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Ameli.Api.Controllers;

[ApiController, Route("api/v1/assistant")]
public sealed class AssistantController(AssistantService assistant, ProductInformationService products, CommerceService commerce) : ControllerBase
{
    private Actor Actor => new(Guid.Parse(User.FindFirstValue("sub")!), Guid.Parse(User.FindFirstValue("sid")!));
    [HttpPost("messages"), AllowAnonymous, EnableRateLimiting("auth")]
    public Task<ChatReply> Message(ChatRequest request, CancellationToken ct) =>
        assistant.RespondAsync(User.Identity?.IsAuthenticated == true ? Actor : null, request, ct);
    [HttpGet("products"), AllowAnonymous]
    public Task<List<StoreProductView>> Products([FromQuery] PublicCatalogQuery query, CancellationToken ct) => products.ProductsAsync(query, ct);
    [HttpGet("products/{id:guid}"), AllowAnonymous]
    public Task<StoreProductView> Product(Guid id, CancellationToken ct) => products.ProductAsync(id, ct);
    [HttpGet("products/{id:guid}/image"), AllowAnonymous]
    public Task<ProductImagePayload> Image(Guid id, CancellationToken ct) => products.ImageAsync(id, ct);
    [HttpGet("admin/products/{id:guid}"), Authorize(Roles = Roles.Administrator)]
    public Task<StoreProductView> AdminProduct(Guid id, CancellationToken ct) => products.ProductAsync(id, ct, true);
    [HttpPut("admin/products/{id:guid}"), Authorize(Roles = Roles.Administrator)]
    public Task<StoreProductView> EditProduct(Guid id, ProductPresentationRequest request, CancellationToken ct) =>
        products.SavePresentationAsync(Actor, id, request, HttpOrigin.Create(HttpContext), ct);
    [HttpGet("cart"), Authorize(Roles = Roles.Client)]
    public Task<CartView> Cart(CancellationToken ct) => commerce.CartAsync(Actor, ct);
    [HttpPost("cart"), Authorize(Roles = Roles.Client)]
    public Task<CartAddResult> Add(AddCartRequest request, CancellationToken ct) => commerce.AddAsync(Actor, request, HttpOrigin.Create(HttpContext), ct);
    [HttpPut("cart/{id:guid}"), Authorize(Roles = Roles.Client)]
    public Task<CartView> UpdateCart(Guid id, UpdateCartRequest request, CancellationToken ct) => commerce.UpdateAsync(Actor, id, request, HttpOrigin.Create(HttpContext), ct);
    [HttpDelete("cart/{id:guid}"), Authorize(Roles = Roles.Client)]
    public async Task<IActionResult> Remove(Guid id, CancellationToken ct)
    { await commerce.RemoveAsync(Actor, id, HttpOrigin.Create(HttpContext), ct); return NoContent(); }
    [HttpPost("checkout"), Authorize(Roles = Roles.Client)]
    public Task<OrderView> Checkout(CheckoutRequest request, CancellationToken ct) => commerce.CheckoutAsync(Actor, request, HttpOrigin.Create(HttpContext), ct);
    [HttpGet("orders"), Authorize(Roles = Roles.Client)]
    public Task<List<OrderView>> Orders(CancellationToken ct) => commerce.OrdersAsync(Actor, false, ct);
    [HttpPost("orders/{id:guid}/rating"), Authorize(Roles = Roles.Client)]
    public Task<PurchaseRatingView> Rate(Guid id, PurchaseRatingRequest request, CancellationToken ct) => commerce.RateAsync(Actor, id, request, HttpOrigin.Create(HttpContext), ct);
    [HttpGet("orders/{number}"), Authorize(Roles = Roles.Client)]
    public async Task<ActionResult<OrderView>> Order(string number, CancellationToken ct)
    {
        var order = await commerce.OwnOrderAsync(Actor, number, ct);
        return order is null ? NotFound(new ApiError("order_unavailable", "Verifica el número desde Mis pedidos.")) : Ok(order);
    }
    [HttpGet("staff/orders"), Authorize(Roles = Roles.Administrator + "," + Roles.Logistics)]
    public Task<List<OrderView>> StaffOrders(CancellationToken ct) => commerce.OrdersAsync(Actor, true, ct);
    [HttpPut("staff/orders/{id:guid}"), Authorize(Roles = Roles.Administrator + "," + Roles.Logistics)]
    public Task<OrderView> UpdateOrder(Guid id, OrderUpdateRequest request, CancellationToken ct) =>
        commerce.UpdateOrderAsync(Actor, id, request, HttpOrigin.Create(HttpContext), ct);
    [HttpPost("staff/orders/{id:guid}/prepare"), Authorize(Roles = Roles.Administrator + "," + Roles.Logistics)]
    public Task<OrderView> Prepare(Guid id, CancellationToken ct) => commerce.StartPreparationAsync(Actor, id, HttpOrigin.Create(HttpContext), ct);
    [HttpPut("staff/orders/{id:guid}/packing"), Authorize(Roles = Roles.Administrator + "," + Roles.Logistics)]
    public Task<OrderView> Packing(Guid id, PackingRequest request, CancellationToken ct) => commerce.SavePackingAsync(Actor, id, request, HttpOrigin.Create(HttpContext), ct);
    [HttpPost("staff/orders/{id:guid}/dispatch"), Authorize(Roles = Roles.Administrator + "," + Roles.Logistics)]
    public Task<OrderView> Dispatch(Guid id, DispatchRequest request, CancellationToken ct) => commerce.DispatchAsync(Actor, id, request, HttpOrigin.Create(HttpContext), ct);
    [HttpPost("staff/orders/{id:guid}/deliver"), Authorize(Roles = Roles.Administrator + "," + Roles.Logistics)]
    public Task<OrderView> Deliver(Guid id, DeliveryRequest request, CancellationToken ct) => commerce.DeliverAsync(Actor, id, request, HttpOrigin.Create(HttpContext), ct);
    [HttpPost("staff/orders/{id:guid}/incidents"), Authorize(Roles = Roles.Administrator + "," + Roles.Logistics)]
    public Task<OrderView> Incident(Guid id, OrderIncidentRequest request, CancellationToken ct) => commerce.RecordIncidentAsync(Actor, id, request, HttpOrigin.Create(HttpContext), ct);
    [HttpPut("staff/orders/{id:guid}/reservations"), Authorize(Roles = Roles.Administrator)]
    public Task<OrderView> Reservations(Guid id, ReservationCorrectionRequest request, CancellationToken ct) => commerce.CorrectReservationsAsync(Actor, id, request, HttpOrigin.Create(HttpContext), ct);
}
