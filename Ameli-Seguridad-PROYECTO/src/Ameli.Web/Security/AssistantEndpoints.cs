using System.Security.Claims;
using Ameli.Contracts;
using Microsoft.AspNetCore.Antiforgery;

namespace Ameli.Web.Security;

public static class AssistantEndpoints
{
    public static void MapAssistantEndpoints(this WebApplication app)
    {
        var publicGroup = app.MapGroup("/account/assistant").AllowAnonymous();
        publicGroup.AddEndpointFilter(ValidateForm);
        publicGroup.MapGet("/products", (HttpContext ctx, SecurityApiClient api) =>
            api.PublicAsync<List<StoreProductView>>(HttpMethod.Get, "assistant/products" + ctx.Request.QueryString, ct: ctx.RequestAborted));
        publicGroup.MapGet("/products/{id:guid}", (Guid id, SecurityApiClient api, CancellationToken ct) =>
            api.PublicAsync<StoreProductView>(HttpMethod.Get, $"assistant/products/{id}", ct: ct));
        publicGroup.MapPost("/messages", (ChatRequest request, SecurityApiClient api, HttpContext ctx) =>
            ctx.User.Identity?.IsAuthenticated == true
                ? api.SendAsync<ChatReply>(HttpMethod.Post, "assistant/messages", request, ctx.RequestAborted)
                : api.PublicAsync<ChatReply>(HttpMethod.Post, "assistant/messages", request, ctx.RequestAborted));
        publicGroup.MapGet("/pets", async (HttpContext ctx, SecurityApiClient api) =>
            ctx.User.IsInRole(Roles.Client) ? await api.SendAsync<List<PetView>>(HttpMethod.Get, "customer/pets", ct: ctx.RequestAborted) : new List<PetView>());
        var clients = app.MapGroup("/account/shop").RequireAuthorization(p => p.RequireRole(Roles.Client));
        clients.AddEndpointFilter(ValidateForm);
        clients.MapGet("/cart", (SecurityApiClient api, CancellationToken ct) => api.SendAsync<CartView>(HttpMethod.Get, "assistant/cart", ct: ct));
        clients.MapPost("/cart", (AddCartRequest request, SecurityApiClient api, CancellationToken ct) => api.SendAsync<CartAddResult>(HttpMethod.Post, "assistant/cart", request, ct));
        clients.MapDelete("/cart/{id:guid}", async (Guid id, SecurityApiClient api, CancellationToken ct) =>
        { await api.SendAsync<object>(HttpMethod.Delete, $"assistant/cart/{id}", ct: ct); return Results.NoContent(); });
        clients.MapPost("/checkout", (CheckoutRequest request, SecurityApiClient api, CancellationToken ct) => api.SendAsync<OrderView>(HttpMethod.Post, "assistant/checkout", request, ct));
        clients.MapGet("/orders", (SecurityApiClient api, CancellationToken ct) => api.SendAsync<List<OrderView>>(HttpMethod.Get, "assistant/orders", ct: ct));
        var staff = app.MapGroup("/account/shop/staff").RequireAuthorization(p => p.RequireRole(Roles.Administrator, Roles.Logistics));
        staff.AddEndpointFilter(ValidateForm);
        staff.MapGet("/orders", (SecurityApiClient api, CancellationToken ct) => api.SendAsync<List<OrderView>>(HttpMethod.Get, "assistant/staff/orders", ct: ct));
        staff.MapPut("/orders/{id:guid}", (Guid id, OrderUpdateRequest request, SecurityApiClient api, CancellationToken ct) =>
            api.SendAsync<OrderView>(HttpMethod.Put, $"assistant/staff/orders/{id}", request, ct));
        var admin = app.MapGroup("/account/shop/admin").RequireAuthorization(p => p.RequireRole(Roles.Administrator));
        admin.AddEndpointFilter(ValidateForm);
        admin.MapGet("/products/{id:guid}", (Guid id, SecurityApiClient api, CancellationToken ct) => api.SendAsync<StoreProductView>(HttpMethod.Get, $"assistant/admin/products/{id}", ct: ct));
        admin.MapPut("/products/{id:guid}", (Guid id, ProductPresentationRequest request, SecurityApiClient api, CancellationToken ct) =>
            api.SendAsync<StoreProductView>(HttpMethod.Put, $"assistant/admin/products/{id}", request, ct));
    }
    private static async ValueTask<object?> ValidateForm(EndpointFilterInvocationContext ctx, EndpointFilterDelegate next)
    {
        if (!HttpMethods.IsGet(ctx.HttpContext.Request.Method))
        {
            try { await ctx.HttpContext.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(ctx.HttpContext); }
            catch (AntiforgeryValidationException) { return Results.BadRequest(new ApiError("invalid_form", "El formulario expiró. Recarga la página.")); }
        }
        return await next(ctx);
    }
}
