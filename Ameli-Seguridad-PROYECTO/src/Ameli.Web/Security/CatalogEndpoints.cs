using Ameli.Contracts;
using Microsoft.AspNetCore.Antiforgery;

namespace Ameli.Web.Security;

public static class CatalogEndpoints
{
    public static void MapCatalogEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/account/catalogs").RequireAuthorization(p => p.RequireRole(Roles.Administrator));
        group.AddEndpointFilter(async (context, next) =>
        {
            if (!HttpMethods.IsGet(context.HttpContext.Request.Method))
                try { await context.HttpContext.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(context.HttpContext); }
                catch (AntiforgeryValidationException) { return Results.BadRequest(new ApiError("invalid_form", "El formulario expiró. Recarga la página.")); }
            return await next(context);
        });
        group.MapGet("/categories", async (HttpContext context, SecurityApiClient api) => Results.Ok(await api.SendAsync<PagedResult<CategoryView>>(HttpMethod.Get, "admin/catalog/categories" + context.Request.QueryString, ct: context.RequestAborted)));
        group.MapPost("/categories", async (CategoryRequest request, SecurityApiClient api, CancellationToken ct) => Results.Ok(await api.SendAsync<CategoryView>(HttpMethod.Post, "admin/catalog/categories", request, ct)));
        group.MapGet("/categories/{id:guid}", (Guid id, SecurityApiClient api, CancellationToken ct) => api.SendAsync<CategoryView>(HttpMethod.Get, $"admin/catalog/categories/{id}", ct: ct));
        group.MapPut("/categories/{id:guid}", (Guid id, CategoryUpdateRequest request, SecurityApiClient api, CancellationToken ct) => api.SendAsync<CategoryView>(HttpMethod.Put, $"admin/catalog/categories/{id}", request, ct));
        group.MapPut("/categories/{id:guid}/availability", (Guid id, AvailabilityRequest request, SecurityApiClient api, CancellationToken ct) => api.SendAsync<CategoryView>(HttpMethod.Put, $"admin/catalog/categories/{id}/availability", request, ct));
        group.MapGet("/suppliers", async (HttpContext context, SecurityApiClient api) => Results.Ok(await api.SendAsync<PagedResult<SupplierView>>(HttpMethod.Get, "admin/catalog/suppliers" + context.Request.QueryString, ct: context.RequestAborted)));
        group.MapPost("/suppliers", async (SupplierRequest request, SecurityApiClient api, CancellationToken ct) => Results.Ok(await api.SendAsync<SupplierView>(HttpMethod.Post, "admin/catalog/suppliers", request, ct)));
        group.MapGet("/products", async (HttpContext context, SecurityApiClient api) => Results.Ok(await api.SendAsync<PagedResult<BaseProductView>>(HttpMethod.Get, "admin/catalog/products" + context.Request.QueryString, ct: context.RequestAborted)));
        group.MapPost("/products", async (BaseProductRequest request, SecurityApiClient api, CancellationToken ct) => Results.Ok(await api.SendAsync<BaseProductView>(HttpMethod.Post, "admin/catalog/products", request, ct)));
        group.MapGet("/products/{id:guid}", (Guid id, SecurityApiClient api, CancellationToken ct) => api.SendAsync<ProductEditView>(HttpMethod.Get, $"admin/catalog/products/{id}", ct: ct));
        group.MapPut("/products/{id:guid}", (Guid id, ProductUpdateRequest request, SecurityApiClient api, CancellationToken ct) => api.SendAsync<ProductEditView>(HttpMethod.Put, $"admin/catalog/products/{id}", request, ct));
        group.MapPut("/products/{id:guid}/availability", (Guid id, AvailabilityRequest request, SecurityApiClient api, CancellationToken ct) => api.SendAsync<ProductEditView>(HttpMethod.Put, $"admin/catalog/products/{id}/availability", request, ct));
        group.MapGet("/products/{id:guid}/image", async (Guid id, SecurityApiClient api, CancellationToken ct) =>
        {
            var image = await api.SendAsync<ProductImagePayload>(HttpMethod.Get, $"admin/catalog/products/{id}/image", ct: ct);
            return Results.File(image.Content, image.ContentType);
        });
    }
}
