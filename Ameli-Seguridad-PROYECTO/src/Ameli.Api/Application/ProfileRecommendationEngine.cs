using System.Text.Json;
using Ameli.Api.Domain;
using Ameli.Contracts;

namespace Ameli.Api.Application;

public interface IProfileRecommendationEngine
{
    List<ChatProductCard> Recommend(PetProfile pet, IEnumerable<StoreProductView> candidates);
}

// Content-based recommender: cosine similarity of weighted profile/product features.
// Safety filters run before ranking and cannot be overridden by the similarity score.
public sealed class ProfileRecommendationEngine : IProfileRecommendationEngine
{
    private static string[] Read(string json) => JsonSerializer.Deserialize<string[]>(json) ?? [];
    private static Dictionary<string, double> Vector(IEnumerable<(string group, string value, double weight)> features) =>
        features.Where(f => !string.IsNullOrWhiteSpace(f.value)).GroupBy(f => f.group + ":" + ProductSizing.Fold(f.value))
            .ToDictionary(g => g.Key, g => g.First().weight);
    private static double Cosine(Dictionary<string, double> a, Dictionary<string, double> b)
    {
        var norm = Math.Sqrt(a.Values.Sum(v => v * v) * b.Values.Sum(v => v * v));
        return norm == 0 ? 0 : a.Sum(x => x.Value * b.GetValueOrDefault(x.Key)) / norm;
    }
    public List<ChatProductCard> Recommend(PetProfile pet, IEnumerable<StoreProductView> candidates)
    {
        if (!pet.IsActive || !pet.AllergiesDeclared || pet.WeightKg <= 0 || pet.HeightCm <= 0) return [];
        var colors = Read(pet.ColorsJson); var materials = Read(pet.MaterialsJson);
        var garments = Read(pet.GarmentTypesJson); var styles = Read(pet.StylesJson);
        var profile = Vector(colors.Select(v => ("color", v, 1d)).Concat(materials.Select(v => ("material", v, 2d)))
            .Concat(garments.Select(v => ("prenda", v, 2d))).Concat(styles.Select(v => ("estilo", v, 2d))));
        return candidates.Where(p => p.IsActive && p.StockQuantity > 0 && p.Material.Length > 0 &&
                ProductSizing.MatchesSpecies(p.Species, pet.Species) && !ProductSizing.AllergyConflict(p.Material, pet.Allergies))
            .Select(p => new { Product = p, Size = ProductSizing.Evaluate(p, pet.Species, pet.HeightCm, pet.WeightKg),
                Score = Cosine(profile, Vector(p.Colors.Select(v => ("color", v, 1d))
                    .Concat(p.Material.Split([',', ';', '/', '+'], StringSplitOptions.TrimEntries).Select(v => ("material", v, 2d)))
                    .Concat(new[] { ("prenda", p.GarmentType, 2d), ("estilo", p.Style, 2d) }))) })
            .Where(x => x.Size.Status == "compatible").OrderByDescending(x => x.Score).ThenBy(x => x.Product.Name).Take(4)
            .Select(x => new ChatProductCard(x.Product, x.Size.SuggestedSize,
                $"Sus medidas entran en la talla {x.Size.SuggestedSize}; el material no coincide con las alergias declaradas." +
                (x.Score > 0 ? " Coincide con sus preferencias de estilo." : " Opción disponible según sus medidas y especie."), pet.Id)).ToList();
    }
}
