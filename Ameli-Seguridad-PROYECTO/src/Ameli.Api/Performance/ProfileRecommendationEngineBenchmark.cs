using Ameli.Api.Application;
using Ameli.Api.Domain;
using Ameli.Contracts;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;

namespace Ameli.Api.Performance;

[Config(typeof(ProfileRecommendationEngineBenchmarkConfig))]
[MemoryDiagnoser]
[SimpleJob]
public sealed class ProfileRecommendationEngineBenchmark
{
    private readonly ProfileRecommendationEngine _engine = new();
    private PetProfile _completeProfile = null!;
    private PetProfile _incompleteProfile = null!;
    private List<StoreProductView> _catalog120 = null!;
    private List<StoreProductView> _catalog1000 = null!;

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
            ColorsJson = "[\"Azul\",\"Verde\",\"Negro\"]",
            MaterialsJson = "[\"Algodón\",\"Lino\",\"Malla transpirable\"]",
            GarmentTypesJson = "[\"Suéter\",\"Chaleco\",\"Impermeable\"]",
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
            IsActive = true,
            ColorsJson = _completeProfile.ColorsJson,
            MaterialsJson = _completeProfile.MaterialsJson,
            GarmentTypesJson = _completeProfile.GarmentTypesJson,
            StylesJson = _completeProfile.StylesJson
        };

        _catalog120 = BuildCatalog(120, seed: 120);
        _catalog1000 = BuildCatalog(1000, seed: 1000);
    }

    [Benchmark(Baseline = true)]
    public List<ChatProductCard> Recommend_CompleteProfile_Catalog120()
    {
        return _engine.Recommend(_completeProfile, _catalog120);
    }

    [Benchmark]
    public List<ChatProductCard> Recommend_CompleteProfile_Catalog1000()
    {
        return _engine.Recommend(_completeProfile, _catalog1000);
    }

    [Benchmark]
    public List<ChatProductCard> Recommend_IncompleteProfile_Catalog1000()
    {
        return _engine.Recommend(_incompleteProfile, _catalog1000);
    }

    private static List<StoreProductView> BuildCatalog(int count, int seed)
    {
        var rng = new Random(seed);
        var now = new DateTimeOffset(2026, 1, 15, 12, 0, 0, TimeSpan.Zero);
        var materials = new[] { "Algodón", "Lino", "Poliéster", "Lana", "Malla transpirable", "Polar" };
        var colors = new[] { "Azul", "Rojo", "Verde", "Negro", "Beige", "Gris" };
        var garments = new[] { "Suéter", "Chaleco", "Impermeable", "Bandana" };
        var styles = new[] { "Clásico", "Deportivo", "Urbano", "Minimalista" };

        var items = new List<StoreProductView>(count);
        for (var i = 0; i < count; i++)
        {
            var material = materials[i % materials.Length];
            var species = i % 8 == 0 ? "Gato" : "Perro";
            var stock = i % 10 == 0 ? 0 : rng.Next(1, 30);
            var active = i % 13 != 0;

            items.Add(new StoreProductView(
                Guid.NewGuid(),
                $"SKU-{i:D5}",
                $"Producto realista {i}",
                "",
                Guid.NewGuid(),
                "Ropa",
                species,
                material,
                "Ficha técnica",
                rng.Next(9000, 45000),
                stock,
                active,
                "Lavado manual",
                "Ajuste cómodo",
                new[] { colors[i % colors.Length], colors[(i + 2) % colors.Length] },
                garments[i % garments.Length],
                styles[i % styles.Length],
                [
                    new("S", 20, 40, 2, 10, "Pequeña"),
                    new("M", 40, 60, 10, 25, "Mediana"),
                    new("L", 60, 80, 25, 40, "Grande")
                ],
                now.AddMinutes(-i),
                $"/productos/{i}",
                $"/productos/{i}/guia"));
        }

        return items;
    }

    private sealed class ProfileRecommendationEngineBenchmarkConfig : ManualConfig
    {
        public ProfileRecommendationEngineBenchmarkConfig()
        {
            AddColumn(StatisticColumn.P95, StatisticColumn.P100, StatisticColumn.OperationsPerSecond);
        }
    }
}
