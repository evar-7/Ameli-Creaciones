using System.Text.Json;
using System.Text.RegularExpressions;
using Ameli.Api.Domain;
using Ameli.Api.Infrastructure;
using Ameli.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Ameli.Api.Application;

public sealed class ProductInformationService(SecurityDbContext db, SqlSecurityTransaction transactions, TimeProvider clock)
{
    private IQueryable<BaseProduct> Active => db.BaseProducts.AsNoTracking().Where(p => p.IsActive &&
        db.Categories.Any(c => c.Id == p.CategoryId && c.IsActive));

    public async Task<List<StoreProductView>> ProductsAsync(string? search, CancellationToken ct)
    {
        var query = Active;
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(p => p.Name.Contains(term) || p.Sku.Contains(term) || p.Material.Contains(term) || p.Description.Contains(term));
        }
        return await ViewsAsync(await query.OrderBy(p => p.Name).Take(30).ToListAsync(ct), ct);
    }
    public async Task<List<StoreProductView>> IdentifyAsync(string message, CancellationToken ct)
    {
        var text = ProductSizing.Fold(message);
        var index = await Active.Select(p => new { p.Id, p.Name, p.Sku }).ToListAsync(ct);
        var exact = index.Where(p => text.Contains(ProductSizing.Fold(p.Sku), StringComparison.Ordinal) ||
            text.Contains(ProductSizing.Fold(p.Name), StringComparison.Ordinal)).Select(p => p.Id).Take(10).ToArray();
        if (exact.Length == 0)
        {
            var stop = new HashSet<string> { "que", "hay", "tienes", "tienen", "precio", "stock", "producto", "productos", "material", "materiales", "disponible", "disponibilidad", "catalogo", "quiero", "ver", "de", "del", "el", "la", "los", "las", "un", "una", "para", "cuanto", "cuesta", "tallas", "talla", "guia", "por", "favor", "es" };
            var terms = Regex.Split(text, @"[^\p{L}\p{N}-]+").Where(t => t.Length > 1 && !stop.Contains(t)).Take(8).ToArray();
            if (terms.Length == 0) return await ProductsAsync(null, ct);
            exact = index.Where(p => terms.All(t => ProductSizing.Fold(p.Name).Contains(t, StringComparison.Ordinal) ||
                ProductSizing.Fold(p.Sku).Contains(t, StringComparison.Ordinal))).Select(p => p.Id).Take(10).ToArray();
            if (exact.Length == 0) return await ProductsAsync(string.Join(" ", terms), ct);
        }
        return await ViewsAsync(await Active.Where(p => exact.Contains(p.Id)).ToListAsync(ct), ct);
    }
    public async Task<StoreProductView> ProductAsync(Guid id, CancellationToken ct, bool includeInactive = false)
    {
        var product = await (includeInactive ? db.BaseProducts.AsNoTracking() : Active).SingleOrDefaultAsync(p => p.Id == id, ct)
            ?? throw new SecurityFault("product_not_found", "Producto no encontrado.", 404);
        return (await ViewsAsync([product], ct)).Single();
    }
    public async Task<List<StoreProductView>> AlternativesAsync(StoreProductView product, CancellationToken ct) =>
        await ViewsAsync(await Active.Where(p => p.Id != product.Id && p.CategoryId == product.CategoryId && p.StockQuantity > 0)
            .OrderBy(p => p.Name).Take(4).ToListAsync(ct), ct);

    public async Task<List<StoreProductView>> RecommendationCandidatesAsync(string species, CancellationToken ct) =>
        await ViewsAsync(await Active.Where(p => p.StockQuantity > 0 && p.Material != "" &&
            (p.Species == species || p.Species == ProductSpecies.All)).OrderBy(p => p.Name).ToListAsync(ct), ct);

    private async Task<List<StoreProductView>> ViewsAsync(List<BaseProduct> products, CancellationToken ct)
    {
        var ids = products.Select(p => p.Id).ToArray();
        var categoryIds = products.Select(p => p.CategoryId).Distinct().ToArray();
        var categories = await db.Categories.AsNoTracking().Where(c => categoryIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.Name, ct);
        var sizes = await db.ProductSizes.AsNoTracking().Where(s => ids.Contains(s.ProductId)).OrderBy(s => s.MinHeightCm).ThenBy(s => s.MinWeightKg).ToListAsync(ct);
        return products.Select(p => new StoreProductView(p.Id, p.Sku, p.Name, p.Description, p.CategoryId,
            categories.GetValueOrDefault(p.CategoryId, ""), p.Species, p.Material, p.Icon, p.UnitPrice, p.StockQuantity,
            p.IsActive, p.CareInstructions, p.FitNotes, JsonSerializer.Deserialize<string[]>(p.ColorsJson) ?? [],
            p.GarmentType, p.Style, sizes.Where(s => s.ProductId == p.Id).Select(s => new ProductSizeView(s.Name,
                s.MinHeightCm, s.MaxHeightCm, s.MinWeightKg, s.MaxWeightKg, s.Equivalent)).ToList(), p.UpdatedAtUtc,
            $"/productos/{p.Id}", $"/productos/{p.Id}/guia")).ToList();
    }

    public async Task<StoreProductView> SavePresentationAsync(Actor actor, Guid id, ProductPresentationRequest request, RequestOrigin origin, CancellationToken ct)
    {
        await using var tx = await transactions.BeginAsync(["ameli:catalogs", SqlSecurityTransaction.User(actor.UserId)], ct);
        var admin = await db.Users.SingleOrDefaultAsync(u => u.Id == actor.UserId && u.IsActive && u.RoleName == Roles.Administrator, ct)
            ?? throw new SecurityFault("forbidden", "Acceso no permitido.", 403);
        var errors = new Dictionary<string, string[]>();
        var sku = (request.Sku ?? "").Trim().ToUpperInvariant();
        if (!Regex.IsMatch(sku, "^[A-Z0-9][A-Z0-9_-]{1,39}$")) errors["Sku"] = ["Usa un SKU de 2–40 letras, números, guiones o guiones bajos."];
        if ((request.CareInstructions?.Length ?? 0) > 1000) errors["CareInstructions"] = ["Máximo 1000 caracteres."];
        if ((request.Material?.Length ?? 0) > 80) errors["Material"] = ["Máximo 80 caracteres."];
        if ((request.FitNotes?.Length ?? 0) > 500) errors["FitNotes"] = ["Máximo 500 caracteres."];
        if ((request.GarmentType?.Length ?? 0) > 40 || (request.Style?.Length ?? 0) > 40) errors["Style"] = ["Máximo 40 caracteres por opción."];
        var colors = (request.Colors ?? []).Select(c => c?.Trim() ?? "").Where(c => c.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (colors.Length > 12 || colors.Any(c => c.Length > 40)) errors["Colors"] = ["Máximo 12 colores de 40 caracteres."];
        if (request.UnitPrice is < 0 or > 99999999.99m || decimal.Round(request.UnitPrice, 2) != request.UnitPrice) errors["UnitPrice"] = ["Precio inválido (máximo 2 decimales)."];
        if (request.StockQuantity is < 0 or > 100000) errors["StockQuantity"] = ["Stock entre 0 y 100000."];
        var sizes = request.Sizes ?? [];
        if (sizes.Length > 20) errors["Sizes"] = ["Máximo 20 tallas."];
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var size in sizes)
        {
            if (size is null || string.IsNullOrWhiteSpace(size.Name) || size.Name.Trim().Length > 20 ||
                !names.Add(size.Name.Trim()) || size.MinHeightCm <= 0 || size.MaxHeightCm < size.MinHeightCm || size.MaxHeightCm > 250 ||
                size.MinWeightKg <= 0 || size.MaxWeightKg < size.MinWeightKg || size.MaxWeightKg > 200 || (size.Equivalent?.Length ?? 0) > 80 ||
                new[] { size.MinHeightCm, size.MaxHeightCm, size.MinWeightKg, size.MaxWeightKg }.Any(n => decimal.Round(n, 2) != n))
                errors["Sizes"] = ["Cada talla requiere nombre único, rangos positivos de altura (cm) y peso (kg), máximo 250 cm y 200 kg, con 2 decimales."];
        }
        if (errors.Count > 0) throw new SecurityFault("invalid_fields", "Revisa los datos del producto.") { Errors = errors };
        if (await db.BaseProducts.AnyAsync(p => p.Id != id && p.Sku == sku, ct)) throw new SecurityFault("duplicate_sku", "Ese SKU ya está utilizado.", 409) { Errors = new() { ["Sku"] = ["SKU ya utilizado."] } };
        var product = await db.BaseProducts.SingleOrDefaultAsync(p => p.Id == id, ct) ?? throw new SecurityFault("product_not_found", "Producto no encontrado.", 404);
        product.Sku = sku; product.CareInstructions = request.CareInstructions?.Trim() ?? ""; product.FitNotes = request.FitNotes?.Trim() ?? "";
        product.Material = request.Material?.Trim() ?? "";
        product.ColorsJson = JsonSerializer.Serialize(colors); product.GarmentType = request.GarmentType?.Trim() ?? ""; product.Style = request.Style?.Trim() ?? "";
        product.UnitPrice = request.UnitPrice; product.StockQuantity = request.StockQuantity; product.IsActive = request.IsActive; product.UpdatedAtUtc = clock.GetUtcNow();
        db.ProductSizes.RemoveRange(await db.ProductSizes.Where(s => s.ProductId == id).ToListAsync(ct));
        await db.SaveChangesAsync(ct);
        db.ProductSizes.AddRange(sizes.Select(s => new ProductSizeRule { ProductId = id, Name = s.Name.Trim(), MinHeightCm = s.MinHeightCm,
            MaxHeightCm = s.MaxHeightCm, MinWeightKg = s.MinWeightKg, MaxWeightKg = s.MaxWeightKg, Equivalent = s.Equivalent?.Trim() ?? "" }));
        db.Events.Add(new SecurityEvent { OccurredAtUtc = clock.GetUtcNow(), ActorUserId = admin.Id, ActorName = admin.Name,
            ActorEmail = admin.Email, ActorRole = admin.RoleName, SessionId = actor.SessionId, Action = "product_presentation_update",
            Outcome = "Exitoso", Module = "Catálogos base", Entity = "base_products", EntityId = id.ToString(), Origin = origin.Ip, CorrelationId = origin.CorrelationId });
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return await ProductAsync(id, ct, true);
    }
}
