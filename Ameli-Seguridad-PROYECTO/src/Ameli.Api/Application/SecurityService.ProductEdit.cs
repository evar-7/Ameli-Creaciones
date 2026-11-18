using System.Security.Cryptography;
using System.Text.Json;
using Ameli.Api.Domain;
using Ameli.Api.Infrastructure;
using Ameli.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Ameli.Api.Application;

public sealed partial class SecurityService
{
    private async Task<ProductEditView> ProductEditViewAsync(BaseProduct product, CancellationToken ct)
    {
        var category = await db.Categories.AsNoTracking().Where(item => item.Id == product.CategoryId).Select(item => item.Name).SingleAsync(ct);
        var supplier = await db.Suppliers.AsNoTracking().Where(item => item.Id == product.SupplierId).Select(item => item.Name).SingleAsync(ct);
        var sizes = await db.ProductSizes.AsNoTracking().Where(item => item.ProductId == product.Id).OrderBy(item => item.Name)
            .Select(item => new ProductSizeView(item.Name, item.MinHeightCm, item.MaxHeightCm, item.MinWeightKg, item.MaxWeightKg, item.Equivalent)).ToListAsync(ct);
        return new(View(product, category, supplier), product.Revision, product.UnitCost, product.MinimumStock, sizes,
            product.ImageContentType is null ? null : $"/account/catalogs/products/{product.Id}/image?v={product.Revision:N}");
    }

    public async Task<ProductEditView> ProductForEditAsync(Actor actor, Guid id, CancellationToken ct)
    {
        await RequireAdminAsync(actor, ct);
        var product = await db.BaseProducts.AsNoTracking().SingleOrDefaultAsync(item => item.Id == id, ct)
            ?? throw new SecurityFault("product_not_found", "Producto no encontrado.", 404);
        return await ProductEditViewAsync(product, ct);
    }

    private static string ProductChangeSnapshot(BaseProduct product, IEnumerable<ProductSizeView> sizes) =>
        JsonSerializer.Serialize(new
        {
            product.Name, product.CategoryId, product.SupplierId, product.Description, product.UnitPrice,
            product.UnitCost, product.Material, product.Species, product.MinimumStock, product.StockQuantity,
            product.IsActive, product.Icon, Sizes = sizes,
            ImageHash = product.Image is null ? null : Convert.ToHexString(SHA256.HashData(product.Image)),
            product.ImageContentType
        });

    public async Task<ProductEditView> UpdateProductAsync(Actor actor, Guid id, ProductUpdateRequest request, RequestOrigin origin, CancellationToken ct)
    {
        await using var tx = await transactions.BeginAsync(["ameli:catalogs", SqlSecurityTransaction.User(actor.UserId)], ct);
        var admin = await RequireAdminAsync(actor, ct);
        var product = await db.BaseProducts.SingleOrDefaultAsync(item => item.Id == id, ct)
            ?? throw new SecurityFault("product_not_found", "Producto no encontrado.", 404);
        try
        {
            ValidateInput(request);
            if (request.IsActive != product.IsActive)
                throw new SecurityFault("availability_action_required", "Usa Desactivar o Reactivar para cambiar la disponibilidad.")
                    { Errors = new() { ["IsActive"] = ["El cambio de estado requiere su acción y confirmación propias."] } };
            if (request.Revision != product.Revision)
                throw new SecurityFault("product_conflict", "El producto cambió mientras lo editabas. Revisa la versión vigente antes de guardar.", 409)
                    { Errors = new() { ["Revision"] = ["Hay una versión más reciente del producto."] } };
            var errors = new Dictionary<string, string[]>();
            var name = request.Name.Trim();
            if (name.Length < 2) errors["Name"] = ["Escribe un nombre de al menos 2 caracteres."];
            if (await db.BaseProducts.AnyAsync(item => item.Id != id && item.NormalizedName == NormalizeName(name), ct))
                errors["Name"] = ["Ese nombre ya está registrado."];
            if (!await db.Categories.AnyAsync(item => item.Id == request.CategoryId && (item.IsActive || item.Id == product.CategoryId), ct))
                errors["CategoryId"] = ["Selecciona una categoría válida."];
            if (!await db.Suppliers.AnyAsync(item => item.Id == request.SupplierId && (item.IsActive || item.Id == product.SupplierId), ct))
                errors["SupplierId"] = ["Selecciona un proveedor válido."];
            if (decimal.Round(request.UnitPrice, 2) != request.UnitPrice) errors["UnitPrice"] = ["Usa un máximo de 2 decimales."];
            if (decimal.Round(request.UnitCost, 2) != request.UnitCost) errors["UnitCost"] = ["Usa un máximo de 2 decimales."];
            var requestedSizes = ProductSizeValidation.Validate(request.Sizes, errors);
            (byte[] bytes, string contentType)? image = null;
            if (request.ImageFileName is not null || request.ImageBase64Data is not null)
            {
                try { image = ImageValidation.Validate(new PetPhotoRequest { FileName = request.ImageFileName ?? "", Base64Data = request.ImageBase64Data ?? "" }); }
                catch (SecurityFault fault) { errors["Image"] = [fault.Message]; }
            }
            if (errors.Count > 0)
                throw new SecurityFault("invalid_fields", "Revisa los datos modificados del producto.") { Errors = errors };

            var oldSizes = await db.ProductSizes.Where(item => item.ProductId == id).OrderBy(item => item.Name).ToListAsync(ct);
            var beforeSizes = oldSizes.Select(item => new ProductSizeView(item.Name, item.MinHeightCm, item.MaxHeightCm, item.MinWeightKg, item.MaxWeightKg, item.Equivalent)).ToArray();
            var afterSizes = requestedSizes.Select(item => new ProductSizeView(item.Name.Trim(), item.MinHeightCm, item.MaxHeightCm, item.MinWeightKg, item.MaxWeightKg, item.Equivalent?.Trim() ?? ""))
                .OrderBy(item => item.Name).ToArray();
            var sizesChanged = !beforeSizes.SequenceEqual(afterSizes);
            var before = ProductChangeSnapshot(product, beforeSizes);
            product.Name = name;
            product.NormalizedName = NormalizeName(name);
            product.CategoryId = request.CategoryId!.Value;
            product.SupplierId = request.SupplierId!.Value;
            product.Description = request.Description?.Trim() ?? "";
            product.UnitPrice = request.UnitPrice;
            product.UnitCost = request.UnitCost;
            product.MinimumStock = request.MinimumStock;
            product.StockQuantity = request.StockQuantity;
            product.Material = request.Material?.Trim() ?? "";
            product.Species = request.Species;
            product.IsActive = request.IsActive!.Value;
            product.Icon = request.Icon?.Trim() ?? "";
            var imageChanged = image is not null && (product.Image is null ||
                product.ImageContentType != image.Value.contentType || !product.Image.AsSpan().SequenceEqual(image.Value.bytes));
            if (imageChanged && image is { } selected)
            {
                product.Image = selected.bytes;
                product.ImageContentType = selected.contentType;
            }
            db.ChangeTracker.DetectChanges();
            if (!db.Entry(product).Properties.Any(property => property.IsModified) && !sizesChanged)
            {
                await tx.CommitAsync(ct);
                return await ProductEditViewAsync(product, ct);
            }
            product.UpdatedAtUtc = Now;
            product.Revision = Guid.NewGuid();
            if (sizesChanged)
            {
                db.ProductSizes.RemoveRange(oldSizes);
                await db.SaveChangesAsync(ct);
                db.ProductSizes.AddRange(afterSizes.Select(item => new ProductSizeRule
                {
                    ProductId = id, Name = item.Name, MinHeightCm = item.MinHeightCm, MaxHeightCm = item.MaxHeightCm,
                    MinWeightKg = item.MinWeightKg, MaxWeightKg = item.MaxWeightKg, Equivalent = item.Equivalent
                }));
            }
            var audit = CatalogAudit("catalog_product_update", "base_products", admin, actor, id, origin);
            audit.BeforeJson = before;
            audit.AfterJson = ProductChangeSnapshot(product, afterSizes);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return await ProductEditViewAsync(product, ct);
        }
        catch (SecurityFault fault)
        {
            CatalogAudit("catalog_product_update", "base_products", admin, actor, id, origin, "Rechazado", fault.Code);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            throw;
        }
    }
}
