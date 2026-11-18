using Ameli.Api.Domain;
using Ameli.Api.Infrastructure;
using Ameli.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Ameli.Api.Application;

public sealed partial class SecurityService
{
    private static void CheckCatalogRevision(Guid? supplied, Guid current)
    {
        if (supplied != current)
            throw new SecurityFault("catalog_conflict", "El registro cambió. Revisa la versión vigente antes de guardar.", 409)
                { Errors = new() { ["Revision"] = ["Hay una versión más reciente."] } };
    }

    private static void CheckAvailabilityRequest(AvailabilityRequest request)
    {
        ValidateInput(request);
        if (!request.IsActive && string.IsNullOrWhiteSpace(request.Reason))
            throw new SecurityFault("reason_required", "Indica el motivo de desactivación.")
                { Errors = new() { ["Reason"] = ["El motivo de desactivación es obligatorio."] } };
    }

    public async Task<CategoryView> CategoryForEditAsync(Actor actor, Guid id, CancellationToken ct)
    {
        await RequireAdminAsync(actor, ct);
        return View(await db.Categories.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new SecurityFault("category_not_found", "Categoría no encontrada.", 404));
    }

    public async Task<CategoryView> UpdateCategoryAsync(Actor actor, Guid id, CategoryUpdateRequest request, RequestOrigin origin, CancellationToken ct)
    {
        await using var tx = await transactions.BeginAsync(["ameli:catalogs", SqlSecurityTransaction.User(actor.UserId)], ct);
        var admin = await RequireAdminAsync(actor, ct);
        var category = await db.Categories.SingleOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new SecurityFault("category_not_found", "Categoría no encontrada.", 404);
        try
        {
            request.Name = request.Name?.Trim() ?? ""; request.Description = request.Description?.Trim() ?? "";
            ValidateInput(request); CheckCatalogRevision(request.Revision, category.Revision);
            var normalized = NormalizeName(request.Name);
            if (await db.Categories.AnyAsync(x => x.Id != id && x.NormalizedName == normalized, ct))
                throw new SecurityFault("duplicate_category", "Otra categoría ya utiliza ese nombre.", 409)
                    { Errors = new() { ["Name"] = ["El nombre de la categoría está duplicado."] } };
            if (category.Name == request.Name && category.Description == request.Description)
            { await tx.CommitAsync(ct); return View(category); }
            var before = Snapshot(category);
            category.Name = request.Name; category.NormalizedName = normalized; category.Description = request.Description;
            category.Revision = Guid.NewGuid(); category.UpdatedAtUtc = Now;
            var audit = CatalogAudit("catalog_category_update", "product_categories", admin, actor, id, origin);
            audit.BeforeJson = before; audit.AfterJson = Snapshot(category);
            await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return View(category);
        }
        catch (SecurityFault fault)
        {
            CatalogAudit("catalog_category_update", "product_categories", admin, actor, id, origin, "Rechazado", fault.Code);
            await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); throw;
        }
    }

    public async Task<CategoryView> CategoryAvailabilityAsync(Actor actor, Guid id, AvailabilityRequest request, RequestOrigin origin, CancellationToken ct)
    {
        await using var tx = await transactions.BeginAsync(["ameli:catalogs", SqlSecurityTransaction.User(actor.UserId)], ct);
        var admin = await RequireAdminAsync(actor, ct);
        var category = await db.Categories.SingleOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new SecurityFault("category_not_found", "Categoría no encontrada.", 404);
        var action = request.IsActive ? "catalog_category_reactivate" : "catalog_category_deactivate";
        try
        {
            if (category.IsActive == request.IsActive) { await tx.CommitAsync(ct); return View(category); }
            CheckAvailabilityRequest(request); CheckCatalogRevision(request.Revision, category.Revision);
            if (!request.IsActive)
            {
                var count = await db.BaseProducts.CountAsync(x => x.CategoryId == id && x.IsActive, ct);
                if (count > 0)
                    throw new SecurityFault("category_has_active_products", $"La categoría tiene {count} producto(s) activo(s). Reasígnalos o desactívalos primero.", 409)
                        { Errors = new() { ["IsActive"] = [$"Productos activos afectados: {count}."] } };
            }
            else
            {
                ValidateInput(new CategoryUpdateRequest { Name = category.Name, Description = category.Description, Revision = category.Revision });
                if (await db.Categories.AnyAsync(x => x.Id != id && x.NormalizedName == NormalizeName(category.Name), ct))
                    throw new SecurityFault("duplicate_category", "El nombre de la categoría está duplicado.", 409)
                        { Errors = new() { ["Name"] = ["El nombre debe ser único."] } };
            }
            var before = Snapshot(category); category.IsActive = request.IsActive;
            category.Revision = Guid.NewGuid(); category.UpdatedAtUtc = Now;
            var audit = CatalogAudit(action, "product_categories", admin, actor, id, origin, detail: request.Reason?.Trim() ?? "");
            audit.BeforeJson = before; audit.AfterJson = Snapshot(category);
            await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return View(category);
        }
        catch (SecurityFault fault)
        {
            CatalogAudit(action, "product_categories", admin, actor, id, origin, "Rechazado", fault.Code);
            await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); throw;
        }
    }

    public async Task<ProductEditView> ProductAvailabilityAsync(Actor actor, Guid id, AvailabilityRequest request, RequestOrigin origin, CancellationToken ct)
    {
        // The checkout holds this same lock from payment validation through order creation.
        await using var tx = await transactions.BeginAsync(["ameli:catalogs", SqlSecurityTransaction.User(actor.UserId)], ct);
        var admin = await RequireAdminAsync(actor, ct);
        var product = await db.BaseProducts.SingleOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new SecurityFault("product_not_found", "Producto no encontrado.", 404);
        var action = request.IsActive ? "catalog_product_reactivate" : "catalog_product_deactivate";
        try
        {
            if (product.IsActive == request.IsActive) { await tx.CommitAsync(ct); return await ProductEditViewAsync(product, ct); }
            CheckAvailabilityRequest(request); CheckCatalogRevision(request.Revision, product.Revision);
            if (request.IsActive)
            {
                ValidateInput(new BaseProductRequest { Name = product.Name, Description = product.Description,
                    CategoryId = product.CategoryId, SupplierId = product.SupplierId, Species = product.Species,
                    Material = product.Material, Icon = product.Icon, UnitPrice = product.UnitPrice,
                    StockQuantity = product.StockQuantity, IsActive = true });
                var errors = new Dictionary<string, string[]>();
                if (!await db.Categories.AnyAsync(x => x.Id == product.CategoryId && x.IsActive, ct)) errors["CategoryId"] = ["La categoría debe estar activa."];
                if (!await db.Suppliers.AnyAsync(x => x.Id == product.SupplierId && x.IsActive, ct)) errors["SupplierId"] = ["El proveedor debe estar activo."];
                if (product.UnitCost < 0 || decimal.Round(product.UnitPrice, 2) != product.UnitPrice || decimal.Round(product.UnitCost, 2) != product.UnitCost)
                    errors["UnitPrice"] = ["Revisa el precio y costo del producto."];
                if (errors.Count > 0) throw new SecurityFault("invalid_reactivation", "Corrige los datos del producto antes de reactivarlo.") { Errors = errors };
            }
            var before = Snapshot(product); product.IsActive = request.IsActive;
            product.Revision = Guid.NewGuid(); product.UpdatedAtUtc = Now;
            var audit = CatalogAudit(action, "base_products", admin, actor, id, origin, detail: request.Reason?.Trim() ?? "");
            audit.BeforeJson = before; audit.AfterJson = Snapshot(product);
            await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return await ProductEditViewAsync(product, ct);
        }
        catch (SecurityFault fault)
        {
            CatalogAudit(action, "base_products", admin, actor, id, origin, "Rechazado", fault.Code);
            await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); throw;
        }
    }
}
