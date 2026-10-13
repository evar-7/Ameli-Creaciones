using Ameli.Api.Application;
using Ameli.Api.Domain;
using Ameli.Contracts;
using Xunit;

namespace Ameli.Security.Tests;

public sealed class AssistantRulesTests
{
    internal static StoreProductView Product(string name = "Suéter azul", int stock = 5, string material = "Algodón", string color = "Azul") =>
        new(Guid.NewGuid(), "SKU-AZUL", name, "", Guid.NewGuid(), "Ropa", "Perro", material, "", 12000, stock, true,
            "Lavado manual", "Ajuste holgado", [color], "Suéter", "Clásico",
            [new("S", 20, 40, 2, 10, "Pequeña"), new("M", 40, 60, 10, 25, "Mediana")], DateTimeOffset.UtcNow, "/productos/1", "/productos/1/guia");
    [Fact]
    public void AAC002_SizeUsesTheProductTableAndDoesNotGuessAtBoundaries()
    {
        var product = Product();
        Assert.Equal("S", ProductSizing.Evaluate(product, "Perro", 30, 8).SuggestedSize);
        var boundary = ProductSizing.Evaluate(product, "Perro", 40, 10);
        Assert.Equal("ambiguous", boundary.Status); Assert.Null(boundary.SuggestedSize);
        Assert.Equal("ambiguous", ProductSizing.Evaluate(product, "Perro", 80, 30).Status);
        Assert.Equal("incompatible", ProductSizing.Evaluate(product, "Gato", 30, 8).Status);
        Assert.Null(ProductSizing.Evaluate(product with { Sizes = [] }, "Perro", 30, 8).SuggestedSize);
        Assert.False(ProductSizing.Positive("8 lb", out _)); Assert.False(ProductSizing.Positive("-8", out _));
    }
    [Fact]
    public void AAC004_EngineFiltersBeforeRankingAndUsesStyleSimilarity()
    {
        var pet = new PetProfile { Species = "Perro", WeightKg = 8, HeightCm = 30, Allergies = "Lana",
            AllergiesDeclared = true, ColorsJson = "[\"Azul\"]" };
        var engine = new ProfileRecommendationEngine();
        var ranked = engine.Recommend(pet, [Product("A rojo", color: "Rojo"), Product("Z azul"),
            Product("Agotado", stock: 0), Product("Alérgico", material: "Lana"), Product("Inactivo") with { IsActive = false }]);
        Assert.Equal("Z azul", ranked[0].Product.Name); Assert.Equal(2, ranked.Count);
        Assert.All(ranked, card => Assert.Equal("S", card.SuggestedSize));
        pet.AllergiesDeclared = false; Assert.Empty(engine.Recommend(pet, [Product()]));
        pet.AllergiesDeclared = true; pet.IsActive = false; Assert.Empty(engine.Recommend(pet, [Product()]));
    }
}
