using System.Globalization;
using System.Text;
using Ameli.Contracts;

namespace Ameli.Api.Application;

public static class ProductSizing
{
    public static string Fold(string value) => new string(value.ToLowerInvariant().Normalize(NormalizationForm.FormD)
        .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray())
        .Normalize(NormalizationForm.FormC);

    public static bool AllergyConflict(string material, string allergies)
    {
        var words = material.Split([',', ';', '/', '+'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(Fold).Where(x => x.Length > 0).ToArray();
        return allergies.Split([',', ';', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(Fold).Any(allergy => words.Any(word => allergy.Contains(word, StringComparison.Ordinal) || word.Contains(allergy, StringComparison.Ordinal)));
    }
    public static bool MatchesSpecies(string productSpecies, string petSpecies) =>
        Fold(productSpecies) == Fold(ProductSpecies.All) || Fold(productSpecies) == Fold(petSpecies);

    public static bool Positive(string? value, out decimal number) =>
        decimal.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out number) && number > 0;

    public static SizeAdvice Evaluate(StoreProductView product, string species, decimal height, decimal weight)
    {
        if (!MatchesSpecies(product.Species, species)) return new("incompatible", null,
            "Este producto no está indicado para la especie seleccionada.", product.Sizes, product.FitNotes, product.GuideUrl);
        if (product.Sizes.Count == 0) return new("unconfigured", null,
            "Este producto todavía no tiene una tabla de tallas publicada. No puedo calcular una talla.", product.Sizes, product.FitNotes, product.GuideUrl);
        var matches = product.Sizes.Where(s => height >= s.MinHeightCm && height <= s.MaxHeightCm &&
            weight >= s.MinWeightKg && weight <= s.MaxWeightKg).ToList();
        if (matches.Count == 1) return new("compatible", matches[0].Name,
            $"Talla sugerida: {matches[0].Name}, según la altura y el peso de la tabla de este producto. Verifica también el ajuste indicado en su guía visual.",
            product.Sizes, product.FitNotes, product.GuideUrl);
        return new("ambiguous", null, matches.Count > 1
            ? $"Las medidas coinciden con varias tallas ({string.Join(", ", matches.Select(x => x.Name))}). Revisa el tipo de ajuste y vuelve a medir; no puedo asegurar una sola talla."
            : "Las medidas no coinciden de forma segura con una sola talla de esta tabla. Pueden estar entre tallas o fuera de rango; revisa el ajuste y vuelve a medir.",
            product.Sizes, product.FitNotes, product.GuideUrl);
    }
}
