using System.Security.Cryptography;
using System.Text;
using Ameli.Api.Domain;
using Ameli.Api.Infrastructure;
using Ameli.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Ameli.Api.Application;

public sealed class RecommendationService(SecurityDbContext db, ProductInformationService products,
    IRecommendationProfileProvider profiles, TimeProvider clock)
{
    private const string RulesVersion = "IAR-RULES-V2";
    private DateTimeOffset Now => clock.GetUtcNow();

    private sealed record FeedbackSignals(
        Dictionary<Guid, Ameli.Api.Domain.RecommendationFeedback> LatestByProduct,
        Dictionary<Guid, (string Material, string Style, string ColorsJson, decimal UnitPrice)> RatedProducts);

    private async Task EnsureClientAsync(Actor actor, CancellationToken ct)
    {
        var ok = await db.Users.AnyAsync(u => u.Id == actor.UserId && u.IsActive && u.RoleName == Roles.Client, ct);
        if (!ok) throw new SecurityFault("forbidden", "Esta función requiere una cuenta de cliente.", 403);
    }

    private static List<RecommendationMissingField> Missing(RecommendationProfile profile)
    {
        var pet = profile.Pet;
        List<RecommendationMissingField> missing = [];
        if (!pet.IsActive) missing.Add(new("IsActive", "Reactivar perfil"));
        if (string.IsNullOrWhiteSpace(pet.Species)) missing.Add(new("Species", "Especie"));
        if (pet.HeightCm <= 0) missing.Add(new("HeightCm", "Talla / altura (cm)"));
        if (pet.WeightKg <= 0) missing.Add(new("WeightKg", "Peso (kg)"));
        if (!pet.AllergiesDeclared) missing.Add(new("AllergiesDeclared", "Declarar alergias o confirmar que no tiene alergias"));
        return missing;
    }

    private static string Token(Guid petId, Guid petRevision, DateTimeOffset petUpdated, DateTimeOffset? prefUpdated,
        StoreProductView product, int trend, string confidence)
    {
        var text = $"{RulesVersion}|{petId}|{petRevision}|{petUpdated.UtcTicks}|{prefUpdated?.UtcTicks}|{product.Id}|{product.UpdatedAtUtc.UtcTicks}|{product.StockQuantity}|{trend}|{confidence}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }

    private static decimal PreferenceScore(RecommendationProfile profile, StoreProductView product)
    {
        static int Hit(IEnumerable<string> values, string source) => values.Count(v => ProductSizing.Fold(source).Contains(ProductSizing.Fold(v), StringComparison.Ordinal));
        var score = 0m;
        score += Math.Min(2, Hit(profile.Colors, string.Join(',', product.Colors))) * 8m;
        score += Math.Min(2, Hit(profile.Materials, product.Material)) * 12m;
        score += Math.Min(1, Hit(profile.GarmentTypes, product.GarmentType)) * 10m;
        score += Math.Min(1, Hit(profile.Styles, product.Style)) * 10m;
        return score;
    }

    private static decimal ClimateScore(StoreProductView product, DateTimeOffset now)
    {
        var material = ProductSizing.Fold(product.Material);
        var warm = now.Month is >= 1 and <= 4;
        var breathable = new[] { "algodon", "lino", "malla", "transpirable" };
        var rainy = new[] { "impermeable", "forro", "polar", "franela" };
        if (warm && breathable.Any(material.Contains)) return 8m;
        if (!warm && rainy.Any(material.Contains)) return 8m;
        return 3m;
    }

    private static string Confidence(RecommendationProfile profile, StoreProductView product, decimal score)
    {
        var optionalMissing = profile.Colors.Length + profile.Materials.Length + profile.GarmentTypes.Length + profile.Styles.Length == 0;
        if (optionalMissing || string.IsNullOrWhiteSpace(product.Style) || string.IsNullOrWhiteSpace(product.GarmentType)) return RecommendationConfidence.Low;
        if (score >= 70m) return RecommendationConfidence.High;
        if (score >= 55m) return RecommendationConfidence.Medium;
        return RecommendationConfidence.Low;
    }

    private async Task<FeedbackSignals> LoadFeedbackSignalsAsync(Actor actor, Guid petId, CancellationToken ct)
    {
        var feedback = await db.RecommendationFeedback.AsNoTracking()
            .Where(x => x.OwnerUserId == actor.UserId && x.PetId == petId)
            .OrderByDescending(x => x.UpdatedAtUtc).ToListAsync(ct);
        var latestByProduct = feedback.GroupBy(x => x.ProductId).ToDictionary(g => g.Key, g => g.First());
        var productIds = latestByProduct.Keys.ToArray();
        var ratedProducts = await db.BaseProducts.AsNoTracking().Where(x => productIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => (x.Material, x.Style, x.ColorsJson, x.UnitPrice), ct);
        return new(latestByProduct, ratedProducts);
    }

    private static decimal FeedbackScore(FeedbackSignals signals, StoreProductView product)
    {
        static bool Overlap(string[] left, string[] right) => left.Select(ProductSizing.Fold).Intersect(right.Select(ProductSizing.Fold)).Any();
        decimal score = 0m;
        if (signals.LatestByProduct.TryGetValue(product.Id, out var direct))
            score += direct.Value == RecommendationFeedbackValue.Useful ? 15m : -20m;
        foreach (var pair in signals.LatestByProduct)
        {
            if (!signals.RatedProducts.TryGetValue(pair.Key, out var rated)) continue;
            var value = pair.Value.Value;
            var positive = value == RecommendationFeedbackValue.Useful;
            var negative = value == RecommendationFeedbackValue.NotInterested;
            if (!positive && !negative) continue;
            var sign = positive ? 1m : -1m;
            var code = ProductSizing.Fold(pair.Value.ReasonCode);
            if (code == "material" && ProductSizing.Fold(product.Material) == ProductSizing.Fold(rated.Material)) score += sign * 8m;
            if (code == "style" && ProductSizing.Fold(product.Style) == ProductSizing.Fold(rated.Style)) score += sign * 8m;
            if (code == "color")
            {
                var a = product.Colors ?? [];
                var b = System.Text.Json.JsonSerializer.Deserialize<string[]>(rated.ColorsJson) ?? [];
                if (Overlap(a, b)) score += sign * 8m;
            }
            if (code == "price")
            {
                if (negative && product.UnitPrice > rated.UnitPrice) score -= 6m;
                if (positive && product.UnitPrice <= rated.UnitPrice) score += 4m;
            }
        }
        return score;
    }

    private static RecommendationAlternativeView Alternative(StoreProductView product, string size, string confidence) =>
        new(product.Id, product.Name, product.CategoryName, product.Icon, product.UnitPrice, product.StockQuantity, size, confidence, true);

    private static RecommendationProductView Item(StoreProductView product, string size, string confidence, string token,
        string reason, bool isAvailable = true) =>
        new(product.Id, product.Name, product.CategoryName, product.Icon, product.UnitPrice, product.StockQuantity,
            size, confidence, token, isAvailable, isAvailable, reason);

    private async Task<List<RecommendationAlternativeView>> AlternativesAsync(RecommendationProfile profile, Guid? excludeProductId, CancellationToken ct)
    {
        var pet = profile.Pet;
        var productsData = await (from p in db.BaseProducts.AsNoTracking()
                                  join c in db.Categories.AsNoTracking() on p.CategoryId equals c.Id
                                  where p.IsActive && c.IsActive && p.StockQuantity > 0
                                        && (p.Species == pet.Species || p.Species == ProductSpecies.All)
                                        && (!excludeProductId.HasValue || p.Id != excludeProductId.Value)
                                  orderby p.Name
                                  select new { p.Id, p.Name, CategoryName = c.Name, p.Icon, p.UnitPrice, p.StockQuantity, p.Material }).ToListAsync(ct);
        var ids = productsData.Select(x => x.Id).ToArray();
        var sizes = await db.ProductSizes.AsNoTracking().Where(x => ids.Contains(x.ProductId)).ToListAsync(ct);
        var list = new List<RecommendationAlternativeView>();
        foreach (var p in productsData)
        {
            if (string.IsNullOrWhiteSpace(p.Material) || ProductSizing.AllergyConflict(p.Material, pet.Allergies)) continue;
            var size = sizes.Where(s => s.ProductId == p.Id && pet.HeightCm >= s.MinHeightCm && pet.HeightCm <= s.MaxHeightCm
                && pet.WeightKg >= s.MinWeightKg && pet.WeightKg <= s.MaxWeightKg).OrderBy(s => s.MinHeightCm).FirstOrDefault();
            if (size is null) continue;
            list.Add(new RecommendationAlternativeView(p.Id, p.Name, p.CategoryName, p.Icon, p.UnitPrice, p.StockQuantity, size.Name, RecommendationConfidence.Low, true));
            if (list.Count == 4) break;
        }
        return list;
    }

    public async Task<RecommendationResultView> RecommendationsAsync(Actor actor, RecommendationQuery query, CancellationToken ct)
    {
        await EnsureClientAsync(actor, ct);
        if (query.PetId is null || query.PetId == Guid.Empty) throw new SecurityFault("missing_pet", "Selecciona una mascota para continuar.");
        var profile = await profiles.GetAsync(actor.UserId, query.PetId.Value, ct)
            ?? throw new SecurityFault("pet_unavailable", "No se encontró el perfil de mascota.", 404);
        var pet = profile.Pet;
        var petView = new RecommendationPetView(pet.Id, pet.Name, pet.Species, pet.Breed, pet.Revision, pet.UpdatedAtUtc, pet.PreferencesUpdatedAtUtc, pet.IsActive);
        var missing = Missing(profile);
        if (missing.Count > 0)
            return new("incomplete_pet", "Completa el perfil para recibir recomendaciones seguras.", petView, missing, [], [], Now,
                $"/perfil/mascotas/{pet.Id}", "Completar perfil");

        try
        {
            var candidates = await products.RecommendationCandidatesAsync(pet.Species, ct);
            var ids = candidates.Select(x => x.Id).ToArray();
            var trendWindow = Now.AddDays(-90);
            var trends = await (from item in db.OrderItems.AsNoTracking()
                                join order in db.Orders.AsNoTracking() on item.OrderId equals order.Id
                                where ids.Contains(item.ProductId) && order.CreatedAtUtc >= trendWindow && order.Status != OrderStatuses.Cancelled
                                group item by item.ProductId into grp
                                select new { ProductId = grp.Key, Quantity = grp.Sum(x => x.Quantity) }).ToDictionaryAsync(x => x.ProductId, x => x.Quantity, ct);
            var feedbackSignals = await LoadFeedbackSignalsAsync(actor, pet.Id, ct);

            var matches = new List<(StoreProductView Product, string Size, string Confidence, string Reason, decimal Score, int Trend)>();
            foreach (var product in candidates)
            {
                if (product.StockQuantity <= 0) continue;
                if (!ProductSizing.MatchesSpecies(product.Species, pet.Species)) continue;
                if (string.IsNullOrWhiteSpace(product.Material) || ProductSizing.AllergyConflict(product.Material, pet.Allergies)) continue;
                var size = ProductSizing.Evaluate(product, pet.Species, pet.HeightCm, pet.WeightKg);
                if (size.Status != "compatible" || string.IsNullOrWhiteSpace(size.SuggestedSize)) continue;
                var trend = trends.GetValueOrDefault(product.Id);
                var feedback = FeedbackScore(feedbackSignals, product);
                var score = 30m + PreferenceScore(profile, product) + ClimateScore(product, Now) + Math.Min(20m, trend * 1.5m) + feedback;
                var confidence = Confidence(profile, product, score);
                var reason = confidence == RecommendationConfidence.Low
                    ? "Coincide con la especie, talla segura y material compatible. Completa más preferencias para mejorar la precisión."
                    : "Coincide con especie, talla segura, material compatible y preferencias registradas.";
                matches.Add((product, size.SuggestedSize!, confidence, reason, score, trend));
            }

            if (matches.Count == 0)
            {
                var alternatives = await AlternativesAsync(profile, null, ct);
                return new("no_matches", "No hay productos compatibles con talla, especie, alergias y stock disponible. Ajusta preferencias opcionales sin omitir restricciones de seguridad.",
                    petView, [], [], alternatives, Now, $"/perfil/mascotas/{pet.Id}/preferencias", "Ajustar preferencias");
            }

            var ordered = matches.OrderByDescending(x => x.Score).ThenBy(x => x.Product.Name).Take(8).Select(x =>
            {
                var token = Token(pet.Id, pet.Revision, pet.UpdatedAtUtc, pet.PreferencesUpdatedAtUtc, x.Product, x.Trend, x.Confidence);
                return Item(x.Product, x.Size, x.Confidence, token, x.Reason);
            }).ToList();

            return new("ok", $"Estas recomendaciones se calcularon para {pet.Name}.", petView, [], ordered, [], Now, "/productos", "Ver catálogo");
        }
        catch
        {
            var alternatives = await AlternativesAsync(profile, null, ct);
            return new("fallback", "No se pudo recalcular en este momento. Se muestran alternativas seguras disponibles.",
                petView, [], [], alternatives, Now, "/recomendados", "Reintentar cálculo");
        }
    }

    public async Task<RecommendationExplanationView> ExplanationAsync(Actor actor, RecommendationExplanationQuery query, CancellationToken ct)
    {
        await EnsureClientAsync(actor, ct);
        if (query.PetId is null || query.ProductId is null || string.IsNullOrWhiteSpace(query.ContextToken))
            throw new SecurityFault("invalid_request", "Faltan datos para explicar la recomendación.");
        try
        {
            var profile = await profiles.GetAsync(actor.UserId, query.PetId.Value, ct)
                ?? throw new SecurityFault("pet_unavailable", "No se encontró el perfil de mascota.", 404);
            var product = await products.ProductAsync(query.ProductId.Value, ct);
            var trend = await (from item in db.OrderItems.AsNoTracking()
                               join order in db.Orders.AsNoTracking() on item.OrderId equals order.Id
                               where item.ProductId == product.Id && order.CreatedAtUtc >= Now.AddDays(-90) && order.Status != OrderStatuses.Cancelled
                               select item.Quantity).SumAsync(ct);
            var size = ProductSizing.Evaluate(product, profile.Pet.Species, profile.Pet.HeightCm, profile.Pet.WeightKg);
            var confidence = Confidence(profile, product, 30m + PreferenceScore(profile, product) + ClimateScore(product, Now) + Math.Min(20m, trend * 1.5m));
            var expected = Token(profile.Pet.Id, profile.Pet.Revision, profile.Pet.UpdatedAtUtc, profile.Pet.PreferencesUpdatedAtUtc, product, trend, confidence);
            var outOfStock = product.StockQuantity <= 0;
            var stale = !string.Equals(expected, query.ContextToken.Trim(), StringComparison.OrdinalIgnoreCase)
                || outOfStock || size.Status != "compatible" || ProductSizing.AllergyConflict(product.Material, profile.Pet.Allergies);
            if (stale)
                return new(product.Id, product.Name, confidence, true, false, outOfStock,
                    outOfStock
                        ? "Este producto quedó no disponible. Te mostramos alternativas compatibles con stock activo."
                        : "La explicación quedó desactualizada por cambios en perfil, stock o reglas. Recalcula antes de usar esta sugerencia.",
                    [], [], outOfStock ? await AlternativesAsync(profile, product.Id, ct) : [], "/recomendados", "Recalcular recomendaciones");

            List<string> factors =
            [
                $"Talla compatible: {size.SuggestedSize} según altura {profile.Pet.HeightCm} cm y peso {profile.Pet.WeightKg} kg.",
                "Material seguro: no coincide con alérgenos declarados.",
                $"Preferencias coincidentes: colores/materiales/estilo priorizados ({confidence}).",
                $"Tendencia interna: {trend} compras recientes de este producto o similares.",
                $"Disponibilidad: {product.StockQuantity} unidades activas."
            ];
            var climate = ClimateScore(product, Now) >= 8m
                ? "El material es favorable para la temporada actual."
                : "El material es utilizable, con menor afinidad estacional.";
            factors.Add("Clima: " + climate);

            List<string> missingOptional = [];
            if (profile.Colors.Length == 0) missingOptional.Add("Colores preferidos");
            if (profile.Materials.Length == 0) missingOptional.Add("Materiales preferidos");
            if (profile.GarmentTypes.Length == 0) missingOptional.Add("Tipo de prenda");
            if (profile.Styles.Length == 0) missingOptional.Add("Estilo");

            var summary = confidence == RecommendationConfidence.Low
                ? "Confianza baja: faltan datos opcionales o historial suficiente; completa preferencias para mejorar la precisión."
                : "Recomendación vigente con factores compatibles y disponibilidad activa.";

            return new(product.Id, product.Name, confidence, false, false, false, summary, factors, missingOptional, [],
                $"/perfil/mascotas/{profile.Pet.Id}/preferencias", "Mejorar preferencias");
        }
        catch (SecurityFault) { throw; }
        catch
        {
            return new(query.ProductId.Value, "", RecommendationConfidence.Low, false, true, false,
                "La explicación no está disponible temporalmente. Intenta nuevamente.", [], [], [], "/recomendados", "Reintentar");
        }
    }

    public async Task<RecommendationFeedbackView> SaveFeedbackAsync(Actor actor, RecommendationFeedbackRequest request, CancellationToken ct)
    {
        await EnsureClientAsync(actor, ct);
        if (request.PetId is null || request.PetId == Guid.Empty || request.ProductId is null || request.ProductId == Guid.Empty)
            throw new SecurityFault("invalid_feedback", "Selecciona mascota y producto.");
        var value = (request.Value ?? "").Trim();
        if (value is not RecommendationFeedbackValue.Useful and not RecommendationFeedbackValue.NotInterested)
            throw new SecurityFault("invalid_feedback", "Selecciona una valoración válida.");
        var reasonCode = (request.ReasonCode ?? "").Trim();
        var reasonText = (request.ReasonText ?? "").Trim();
        if (value == RecommendationFeedbackValue.NotInterested && string.IsNullOrWhiteSpace(reasonCode) && string.IsNullOrWhiteSpace(reasonText))
            throw new SecurityFault("missing_reason", "Selecciona un motivo para ocultar esta sugerencia en la sesión actual.");

        var pet = await db.Pets.AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.PetId.Value && x.OwnerUserId == actor.UserId, ct)
            ?? throw new SecurityFault("pet_unavailable", "No se encontró el perfil de mascota.", 404);
        if (!await db.BaseProducts.AsNoTracking().AnyAsync(x => x.Id == request.ProductId.Value, ct))
            throw new SecurityFault("product_not_found", "Producto no encontrado.", 404);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var existing = await db.RecommendationFeedback.SingleOrDefaultAsync(x => x.OwnerUserId == actor.UserId
            && x.PetId == request.PetId.Value && x.ProductId == request.ProductId.Value && x.SessionId == actor.SessionId, ct);
        var replaced = existing is not null;
        if (existing is null)
        {
            existing = new Ameli.Api.Domain.RecommendationFeedback
            {
                OwnerUserId = actor.UserId,
                PetId = request.PetId.Value,
                ProductId = request.ProductId.Value,
                SessionId = actor.SessionId,
                CreatedAtUtc = Now
            };
            db.RecommendationFeedback.Add(existing);
        }
        existing.Value = value;
        existing.ReasonCode = reasonCode;
        existing.ReasonText = reasonText;
        existing.UpdatedAtUtc = Now;
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return new(pet.Id, request.ProductId.Value, existing.Value, existing.ReasonCode, existing.ReasonText, existing.UpdatedAtUtc, replaced);
    }
}
