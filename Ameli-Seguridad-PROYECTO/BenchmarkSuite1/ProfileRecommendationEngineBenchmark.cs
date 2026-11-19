using Ameli.Api.Application;
using Ameli.Api.Domain;
using Ameli.Contracts;
using BenchmarkDotNet.Attributes;
using Microsoft.VSDiagnostics;

namespace Ameli.Api.PerformanceBenchmarks;
[CPUUsageDiagnoser]
public class ProfileRecommendationEngineBenchmark
{
    private readonly ProfileRecommendationEngine _engine = new();
    private PetProfile _completeProfile = null!;
    private PetProfile _incompleteProfile = null!;
    private List<StoreProductView> _catalog60 = null!;
    private List<StoreProductView> _catalog300 = null!;
    [GlobalSetup]
    public void Setup()
    {
        _completeProfile = new PetProfile
        {
            Id = Guid.NewGuid(),
            Species = "Perro",
            WeightKg = 8,
            HeightCm = 30,
            Allergies = "Lana",
            AllergiesDeclared = true,
            IsActive = true,
            ColorsJson = "[\"Azul\",\"Verde\"]",
            MaterialsJson = "[\"Algodón\",\"Lino\"]",
            GarmentTypesJson = "[\"Suéter\",\"Chaleco\"]",
            StylesJson = "[\"Clásico\",\"Deportivo\"]"
        };
        _incompleteProfile = new PetProfile
        {
            Id = _completeProfile.Id,
            Species = _completeProfile.Species,
            WeightKg = _completeProfile.WeightKg,
            HeightCm = _completeProfile.HeightCm,
            Allergies = _completeProfile.Allergies,
            AllergiesDeclared = false,
            IsActive = _completeProfile.IsActive,
            ColorsJson = _completeProfile.ColorsJson,
            MaterialsJson = _completeProfile.MaterialsJson,
            GarmentTypesJson = _completeProfile.GarmentTypesJson,
            StylesJson = _completeProfile.StylesJson
        };
        _catalog60 = BuildCatalog(60);
        _catalog300 = BuildCatalog(300);
    }

    [Benchmark(Baseline = true)]
    public List<ChatProductCard> Recommend_CompleteProfile_Catalog60()
    {
        return _engine.Recommend(_completeProfile, _catalog60);
    }

    [Benchmark]
    public List<ChatProductCard> Recommend_CompleteProfile_Catalog300()
    {
        return _engine.Recommend(_completeProfile, _catalog300);
    }

    [Benchmark]
    public List<ChatProductCard> Recommend_IncompleteProfile_Catalog300()
    {
        return _engine.Recommend(_incompleteProfile, _catalog300);
    }

    private static List<StoreProductView> BuildCatalog(int count)
    {
        var rng = new Random(42 + count);
        var materials = new[]
        {
            "Algodón",
            "Lino",
            "Poliéster",
            "Lana",
            "Malla transpirable"
        };
        var colors = new[]
        {
            "Azul",
            "Rojo",
            "Verde",
            "Negro",
            "Beige"
        };
        var garments = new[]
        {
            "Suéter",
            "Chaleco",
            "Impermeable",
            "Bandana"
        };
        var styles = new[]
        {
            "Clásico",
            "Deportivo",
            "Urbano",
            "Minimalista"
        };
        var items = new List<StoreProductView>(count);
        for (var i = 0; i < count; i++)
        {
            var material = materials[i % materials.Length];
            var specie = i % 7 == 0 ? "Gato" : "Perro";
            var stock = i % 9 == 0 ? 0 : rng.Next(1, 30);
            var active = i % 11 != 0;
            items.Add(new StoreProductView(Guid.NewGuid(), $"SKU-{i:D4}", $"Producto {i}", "", Guid.NewGuid(), "Ropa", specie, material, "Ficha técnica", rng.Next(9000, 45000), stock, active, "Lavado manual", "Ajuste cómodo", new[] { colors[i % colors.Length], colors[(i + 2) % colors.Length] }, garments[i % garments.Length], styles[i % styles.Length], [new("S", 20, 40, 2, 10, "Pequeña"), new("M", 40, 60, 10, 25, "Mediana"), new("L", 60, 80, 25, 40, "Grande")], DateTimeOffset.UtcNow.AddMinutes(-i), $"/productos/{i}", $"/productos/{i}/guia"));
        }

        return items;
    }
}