using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Ameli.Api.Domain;
using Ameli.Api.Infrastructure;
using Ameli.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Ameli.Api.Application;

public sealed partial class SecurityService
{
    private const string CatalogModule = "Catálogos base";
    private static string NormalizeName(string value) => value.Trim().ToUpperInvariant();
    private static string Snapshot(ProductCategory category) => JsonSerializer.Serialize(new { category.Name, category.Description, category.IsActive });
    private static string Snapshot(Supplier supplier) => JsonSerializer.Serialize(new { supplier.Name, supplier.ContactName, supplier.Email, supplier.Phone, supplier.Notes, supplier.IsActive });
    private static string Snapshot(BaseProduct product) => JsonSerializer.Serialize(new { product.Name, product.Description, product.CategoryId, product.SupplierId, product.Species, product.Material, product.Icon, product.UnitPrice, product.StockQuantity, product.IsActive });
    private static CategoryView View(ProductCategory category) => new(category.Id, category.Name, category.Description, category.IsActive, category.CreatedAtUtc, category.UpdatedAtUtc);
    private static SupplierView View(Supplier supplier) => new(supplier.Id, supplier.Name, supplier.ContactName, supplier.Email, supplier.Phone, supplier.Notes, supplier.IsActive, supplier.CreatedAtUtc, supplier.UpdatedAtUtc);
    private static BaseProductView View(BaseProduct product, string categoryName, string supplierName) => new(product.Id, product.Name, product.Description, product.CategoryId, categoryName, product.SupplierId, supplierName, product.Species, product.Material, product.Icon, product.UnitPrice, product.StockQuantity, product.IsActive, product.CreatedAtUtc, product.UpdatedAtUtc);
    private static void Clean(CategoryRequest request)
    {
        request.Name = request.Name?.Trim() ?? "";
        request.Description = request.Description?.Trim() ?? "";
    }
    private static void Clean(SupplierRequest request)
    {
        request.Name = request.Name?.Trim() ?? "";
        request.ContactName = request.ContactName?.Trim() ?? "";
        request.Email = request.Email?.Trim() ?? "";
        request.Phone = request.Phone?.Trim() ?? "";
        request.Notes = request.Notes?.Trim() ?? "";
    }
    private static void Clean(BaseProductRequest request)
    {
        request.Name = request.Name?.Trim() ?? "";
        request.Description = request.Description?.Trim() ?? "";
        request.Species = request.Species?.Trim() ?? "";
        request.Material = request.Material?.Trim() ?? "";
        request.Icon = request.Icon?.Trim() ?? "";
    }
    private SecurityEvent CatalogAudit(string action, string entity, AppUser admin, Actor actor, Guid? id, RequestOrigin origin, string outcome = "Exitoso", string detail = "")
    {
        var audit = Audit(action, outcome, origin, admin, id, detail);
        audit.Module = CatalogModule;
        audit.Entity = entity;
        audit.EntityId = id?.ToString() ?? "";
        audit.SubjectUserId = null;
        audit.SessionId = actor.SessionId;
        return audit;
    }
    public async Task<PagedResult<CategoryView>> CategoriesAsync(Actor actor, CategoryQuery query, CancellationToken ct)
    {
        await RequireAdminAsync(actor, ct);
        ValidateInput(query);
        var rows = db.Categories.AsNoTracking();
        var search = query.Search?.Trim();
        if (!string.IsNullOrEmpty(search)) rows = rows.Where(x => x.Name.Contains(search) || x.Description.Contains(search));
        if (query.IsActive.HasValue) rows = rows.Where(x => x.IsActive == query.IsActive.Value);
        var total = await rows.CountAsync(ct);
        var items = await rows.OrderBy(x => x.Name).ThenBy(x => x.Id).Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToListAsync(ct);
        return new(items.Select(View).ToList(), total, query.Page, query.PageSize);
    }
    public async Task<PagedResult<SupplierView>> SuppliersAsync(Actor actor, SupplierQuery query, CancellationToken ct)
    {
        await RequireAdminAsync(actor, ct);
        ValidateInput(query);
        var rows = db.Suppliers.AsNoTracking();
        var search = query.Search?.Trim();
        if (!string.IsNullOrEmpty(search)) rows = rows.Where(x => x.Name.Contains(search) || x.ContactName.Contains(search) || x.Email.Contains(search) || x.Phone.Contains(search));
        if (query.IsActive.HasValue) rows = rows.Where(x => x.IsActive == query.IsActive.Value);
        var total = await rows.CountAsync(ct);
        var items = await rows.OrderBy(x => x.Name).ThenBy(x => x.Id).Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToListAsync(ct);
        return new(items.Select(View).ToList(), total, query.Page, query.PageSize);
    }
    public async Task<PagedResult<BaseProductView>> BaseProductsAsync(Actor actor, BaseProductQuery query, CancellationToken ct)
    {
        await RequireAdminAsync(actor, ct);
        ValidateInput(query);
        var rows = from product in db.BaseProducts.AsNoTracking()
                   join category in db.Categories.AsNoTracking() on product.CategoryId equals category.Id
                   join supplier in db.Suppliers.AsNoTracking() on product.SupplierId equals supplier.Id
                   select new { product, category, supplier };
        var search = query.Search?.Trim();
        if (!string.IsNullOrEmpty(search)) rows = rows.Where(x => x.product.Name.Contains(search) || x.product.Description.Contains(search) || x.product.Material.Contains(search) || x.category.Name.Contains(search) || x.supplier.Name.Contains(search));
        if (query.CategoryId.HasValue) rows = rows.Where(x => x.product.CategoryId == query.CategoryId.Value);
        if (query.SupplierId.HasValue) rows = rows.Where(x => x.product.SupplierId == query.SupplierId.Value);
        if (!string.IsNullOrWhiteSpace(query.Species)) rows = rows.Where(x => x.product.Species == query.Species);
        if (query.IsActive.HasValue) rows = rows.Where(x => x.product.IsActive == query.IsActive.Value);
        var total = await rows.CountAsync(ct);
        var items = await rows.OrderBy(x => x.product.Name).ThenBy(x => x.product.Id).Skip((query.Page - 1) * query.PageSize).Take(query.PageSize)
            .Select(x => new BaseProductView(x.product.Id, x.product.Name, x.product.Description, x.category.Id, x.category.Name, x.supplier.Id, x.supplier.Name, x.product.Species, x.product.Material, x.product.Icon, x.product.UnitPrice, x.product.StockQuantity, x.product.IsActive, x.product.CreatedAtUtc, x.product.UpdatedAtUtc))
            .ToListAsync(ct);
        return new(items, total, query.Page, query.PageSize);
    }
    public async Task<CategoryView> CreateCategoryAsync(Actor actor, CategoryRequest request, RequestOrigin origin, CancellationToken ct)
    {
        await using var tx = await transactions.BeginAsync(["ameli:catalogs", SqlSecurityTransaction.User(actor.UserId)], ct);
        var admin = await RequireAdminAsync(actor, ct);
        try
        {
            Clean(request);
            ValidateInput(request);
            var normalized = NormalizeName(request.Name);
            var duplicate = await db.Categories.AsNoTracking().SingleOrDefaultAsync(x => x.NormalizedName == normalized, ct);
            if (duplicate is not null)
                throw new SecurityFault("duplicate_category", "Esa categoría ya está registrada.", 409) { ExistingId = duplicate.Id, Errors = new() { ["Name"] = ["Debe ser única."] } };
            var category = new ProductCategory { Name = request.Name, NormalizedName = normalized, Description = request.Description, IsActive = request.IsActive!.Value, CreatedAtUtc = Now, UpdatedAtUtc = Now };
            db.Categories.Add(category);
            CatalogAudit("catalog_category_create", "product_categories", admin, actor, category.Id, origin).AfterJson = Snapshot(category);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return View(category);
        }
        catch (SecurityFault ex)
        {
            CatalogAudit("catalog_category_create", "product_categories", admin, actor, null, origin, "Rechazado", ex.Code);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            throw;
        }
    }
    public async Task<SupplierView> CreateSupplierAsync(Actor actor, SupplierRequest request, RequestOrigin origin, CancellationToken ct)
    {
        await using var tx = await transactions.BeginAsync(["ameli:catalogs", SqlSecurityTransaction.User(actor.UserId)], ct);
        var admin = await RequireAdminAsync(actor, ct);
        try
        {
            Clean(request);
            ValidateInput(request);
            var normalizedName = NormalizeName(request.Name);
            var normalizedEmail = NormalizeEmail(request.Email);
            var duplicate = await db.Suppliers.AsNoTracking().SingleOrDefaultAsync(x => x.NormalizedName == normalizedName || x.NormalizedEmail == normalizedEmail, ct);
            if (duplicate is not null)
            {
                var errors = new Dictionary<string, string[]>();
                if (duplicate.NormalizedName == normalizedName) errors["Name"] = ["Debe ser único."];
                if (duplicate.NormalizedEmail == normalizedEmail) errors["Email"] = ["Ese correo ya está registrado."];
                throw new SecurityFault("duplicate_supplier", "Ese proveedor ya está registrado.", 409) { ExistingId = duplicate.Id, Errors = errors };
            }
            var supplier = new Supplier { Name = request.Name, NormalizedName = normalizedName, ContactName = request.ContactName, Email = request.Email, NormalizedEmail = normalizedEmail, Phone = request.Phone, Notes = request.Notes, IsActive = request.IsActive!.Value, CreatedAtUtc = Now, UpdatedAtUtc = Now };
            db.Suppliers.Add(supplier);
            CatalogAudit("catalog_supplier_create", "suppliers", admin, actor, supplier.Id, origin).AfterJson = Snapshot(supplier);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return View(supplier);
        }
        catch (SecurityFault ex)
        {
            CatalogAudit("catalog_supplier_create", "suppliers", admin, actor, null, origin, "Rechazado", ex.Code);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            throw;
        }
    }
    public async Task<BaseProductView> CreateBaseProductAsync(Actor actor, BaseProductRequest request, RequestOrigin origin, CancellationToken ct)
    {
        await using var tx = await transactions.BeginAsync(["ameli:catalogs", SqlSecurityTransaction.User(actor.UserId)], ct);
        var admin = await RequireAdminAsync(actor, ct);
        try
        {
            Clean(request);
            ValidateInput(request);
            var category = await db.Categories.AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.CategoryId, ct);
            if (category is null || !category.IsActive)
                throw new SecurityFault("invalid_category", "Selecciona una categoría activa.") { Errors = new() { ["CategoryId"] = ["Selecciona una categoría activa."] } };
            var supplier = await db.Suppliers.AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.SupplierId, ct);
            if (supplier is null || !supplier.IsActive)
                throw new SecurityFault("invalid_supplier", "Selecciona un proveedor activo.") { Errors = new() { ["SupplierId"] = ["Selecciona un proveedor activo."] } };
            var normalized = NormalizeName(request.Name);
            var duplicate = await db.BaseProducts.AsNoTracking().SingleOrDefaultAsync(x => x.NormalizedName == normalized, ct);
            if (duplicate is not null)
                throw new SecurityFault("duplicate_product", "Ese producto base ya está registrado.", 409) { ExistingId = duplicate.Id, Errors = new() { ["Name"] = ["Debe ser único."] } };
            var product = new BaseProduct
            {
                Name = request.Name,
                NormalizedName = normalized,
                Description = request.Description,
                CategoryId = request.CategoryId!.Value,
                SupplierId = request.SupplierId!.Value,
                Species = request.Species,
                Material = request.Material,
                Icon = request.Icon,
                UnitPrice = request.UnitPrice,
                StockQuantity = request.StockQuantity,
                IsActive = request.IsActive!.Value,
                CreatedAtUtc = Now,
                UpdatedAtUtc = Now
            };
            db.BaseProducts.Add(product);
            CatalogAudit("catalog_product_create", "base_products", admin, actor, product.Id, origin).AfterJson = Snapshot(product);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return View(product, category.Name, supplier.Name);
        }
        catch (SecurityFault ex)
        {
            CatalogAudit("catalog_product_create", "base_products", admin, actor, null, origin, "Rechazado", ex.Code);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            throw;
        }
    }
}
