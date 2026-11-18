using System.Security.Claims;
using Ameli.Api.Application;
using Ameli.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ameli.Api.Controllers;

[ApiController, Route("api/v1/admin/catalog"), Authorize(Roles = Roles.Administrator)]
public sealed class CatalogsController(SecurityService service, ProductInformationService products) : ControllerBase
{
    private Actor Actor => new(Guid.Parse(User.FindFirstValue("sub")!), Guid.Parse(User.FindFirstValue("sid")!));

    [HttpGet("categories")]
    public Task<PagedResult<CategoryView>> Categories([FromQuery] CategoryQuery query, CancellationToken ct) => service.CategoriesAsync(Actor, query, ct);

    [HttpPost("categories")]
    public Task<CategoryView> CreateCategory(CategoryRequest request, CancellationToken ct) => service.CreateCategoryAsync(Actor, request, HttpOrigin.Create(HttpContext), ct);
    [HttpGet("categories/{id:guid}")]
    public Task<CategoryView> Category(Guid id, CancellationToken ct) => service.CategoryForEditAsync(Actor, id, ct);
    [HttpPut("categories/{id:guid}")]
    public Task<CategoryView> UpdateCategory(Guid id, CategoryUpdateRequest request, CancellationToken ct) => service.UpdateCategoryAsync(Actor, id, request, HttpOrigin.Create(HttpContext), ct);
    [HttpPut("categories/{id:guid}/availability")]
    public Task<CategoryView> CategoryAvailability(Guid id, AvailabilityRequest request, CancellationToken ct) => service.CategoryAvailabilityAsync(Actor, id, request, HttpOrigin.Create(HttpContext), ct);

    [HttpGet("suppliers")]
    public Task<PagedResult<SupplierView>> Suppliers([FromQuery] SupplierQuery query, CancellationToken ct) => service.SuppliersAsync(Actor, query, ct);

    [HttpPost("suppliers")]
    public Task<SupplierView> CreateSupplier(SupplierRequest request, CancellationToken ct) => service.CreateSupplierAsync(Actor, request, HttpOrigin.Create(HttpContext), ct);

    [HttpGet("products")]
    public Task<PagedResult<BaseProductView>> Products([FromQuery] BaseProductQuery query, CancellationToken ct) => service.BaseProductsAsync(Actor, query, ct);

    [HttpPost("products")]
    public Task<BaseProductView> CreateProduct(BaseProductRequest request, CancellationToken ct) => service.CreateBaseProductAsync(Actor, request, HttpOrigin.Create(HttpContext), ct);
    [HttpGet("products/{id:guid}")]
    public Task<ProductEditView> Product(Guid id, CancellationToken ct) => service.ProductForEditAsync(Actor, id, ct);
    [HttpPut("products/{id:guid}")]
    public Task<ProductEditView> UpdateProduct(Guid id, ProductUpdateRequest request, CancellationToken ct) => service.UpdateProductAsync(Actor, id, request, HttpOrigin.Create(HttpContext), ct);
    [HttpPut("products/{id:guid}/availability")]
    public Task<ProductEditView> ProductAvailability(Guid id, AvailabilityRequest request, CancellationToken ct) => service.ProductAvailabilityAsync(Actor, id, request, HttpOrigin.Create(HttpContext), ct);
    [HttpGet("products/{id:guid}/image")]
    public Task<ProductImagePayload> ProductImage(Guid id, CancellationToken ct) => products.ImageAsync(id, ct, true);
}
