using Ameli.Contracts;

namespace Ameli.Api.Application;

public static class ProductSizeValidation
{
    public static ProductSizeRequest[] Validate(ProductSizeRequest[]? input, Dictionary<string, string[]> errors)
    {
        var sizes = input ?? [];
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
        return sizes;
    }
}
