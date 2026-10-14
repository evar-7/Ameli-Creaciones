using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ameli.Api.Application;
using Ameli.Api.Domain;
using Ameli.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Ameli.Security.Tests.SecurityFixture;

namespace Ameli.Security.Tests;

public sealed class RecommendationStoriesTests(SecurityFixture f) : IClassFixture<SecurityFixture>, IAsyncLifetime
{
    public Task InitializeAsync() => f.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task IAR001_GeneratesCompatibleRecommendationsOrderedByRules()
    {
        var actor = Actor(await f.Login("cliente@ameli.test"));
        var petId = await AddPet(actor.UserId, allergies: "", allergiesDeclared: true, colors: ["lavanda"], materials: ["lona"], garments: ["arnes"], styles: ["confort"]);
        var category = await AddCategory("Accesorios");
        var supplier = await AddSupplier("Taller Lavanda", "lavanda@ameli.test");
        await AddProduct("Arnés confort lavanda", ProductSpecies.Dog, "lona", category, supplier, 12);
        await AddProduct("Arnés básico azul", ProductSpecies.Dog, "nylon", category, supplier, 12);

        var result = await Service(s => s.RecommendationsAsync(actor, new RecommendationQuery { PetId = petId }, default));

        Assert.Equal("ok", result.Status);
        Assert.NotEmpty(result.Items);
        Assert.Equal("Arnés confort lavanda", result.Items[0].Name);
        Assert.All(result.Items, x => Assert.True(x.CanAddToCart));
    }

    [Fact]
    public async Task IAR001_ExcludesAllergicMaterials()
    {
        var actor = Actor(await f.Login("cliente@ameli.test"));
        var petId = await AddPet(actor.UserId, allergies: "cuero", allergiesDeclared: true);
        var category = await AddCategory("Accesorios");
        var supplier = await AddSupplier("Proveedor Uno", "uno@ameli.test");
        await AddProduct("Collar cuero", ProductSpecies.Dog, "cuero", category, supplier, 10);
        await AddProduct("Collar tela", ProductSpecies.Dog, "algodon", category, supplier, 10);

        var result = await Service(s => s.RecommendationsAsync(actor, new RecommendationQuery { PetId = petId }, default));

        Assert.DoesNotContain(result.Items, x => x.Name.Contains("cuero", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Items, x => x.Name.Contains("tela", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task IAR001_ReturnsIncompleteProfileWhenMandatoryDataIsMissing()
    {
        var actor = Actor(await f.Login("cliente@ameli.test"));
        var petId = await AddPet(actor.UserId, height: 0, weight: 0, allergies: "", allergiesDeclared: false);

        var result = await Service(s => s.RecommendationsAsync(actor, new RecommendationQuery { PetId = petId }, default));

        Assert.Equal("incomplete_pet", result.Status);
        Assert.NotEmpty(result.MissingFields);
        Assert.Contains(result.MissingFields, x => x.Field == "HeightCm");
        Assert.Contains(result.MissingFields, x => x.Field == "AllergiesDeclared");
    }

    [Fact]
    public async Task IAR001_ReturnsNoMatchesWhenNoCompatibleStockExists()
    {
        var actor = Actor(await f.Login("cliente@ameli.test"));
        var petId = await AddPet(actor.UserId, allergies: "", allergiesDeclared: true);
        var category = await AddCategory("Ropa");
        var supplier = await AddSupplier("Proveedor Dos", "dos@ameli.test");
        await AddProduct("Suéter gato", ProductSpecies.Cat, "algodon", category, supplier, 10);

        var result = await Service(s => s.RecommendationsAsync(actor, new RecommendationQuery { PetId = petId }, default));

        Assert.Equal("no_matches", result.Status);
        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task IAR002_ExplanationShowsFactorsAndLowConfidenceWhenOptionalDataMissing()
    {
        var actor = Actor(await f.Login("cliente@ameli.test"));
        var petId = await AddPet(actor.UserId, allergies: "", allergiesDeclared: true, colors: [], materials: [], garments: [], styles: []);
        var category = await AddCategory("Accesorios");
        var supplier = await AddSupplier("Proveedor Tres", "tres@ameli.test");
        await AddProduct("Arnés simple", ProductSpecies.Dog, "algodon", category, supplier, 8);

        var list = await Service(s => s.RecommendationsAsync(actor, new RecommendationQuery { PetId = petId }, default));
        var item = Assert.Single(list.Items);
        var detail = await Service(s => s.ExplanationAsync(actor, new RecommendationExplanationQuery
        {
            PetId = petId,
            ProductId = item.ProductId,
            ContextToken = item.ContextToken
        }, default));

        Assert.False(detail.IsStale);
        Assert.False(detail.IsTemporarilyUnavailable);
        Assert.NotEmpty(detail.Factors);
        Assert.Equal(RecommendationConfidence.Low, detail.Confidence);
        Assert.Contains("Confianza baja", detail.Summary);
    }

    [Fact]
    public async Task IAR002_ExplanationDetectsStaleWhenProfileChanges()
    {
        var actor = Actor(await f.Login("cliente@ameli.test"));
        var petId = await AddPet(actor.UserId, allergies: "", allergiesDeclared: true, colors: ["azul"], materials: ["algodon"], garments: ["arnes"], styles: ["clasico"]);
        var category = await AddCategory("Accesorios");
        var supplier = await AddSupplier("Proveedor Cuatro", "cuatro@ameli.test");
        await AddProduct("Arnés clásico", ProductSpecies.Dog, "algodon", category, supplier, 8);

        var list = await Service(s => s.RecommendationsAsync(actor, new RecommendationQuery { PetId = petId }, default));
        var item = Assert.Single(list.Items);

        await f.Db(async db =>
        {
            var pet = await db.Pets.SingleAsync(x => x.Id == petId);
            pet.WeightKg += 2;
            pet.UpdatedAtUtc = f.Clock.GetUtcNow().AddMinutes(1);
            pet.Revision = Guid.NewGuid();
            await db.SaveChangesAsync();
        });

        var detail = await Service(s => s.ExplanationAsync(actor, new RecommendationExplanationQuery
        {
            PetId = petId,
            ProductId = item.ProductId,
            ContextToken = item.ContextToken
        }, default));

        Assert.True(detail.IsStale);
    }

    [Fact]
    public async Task IAR003_RecalculateByAllergyChangesRemovesIncompatibleAndReorders()
    {
        var actor = Actor(await f.Login("cliente@ameli.test"));
        var petId = await AddPet(actor.UserId, allergies: "", allergiesDeclared: true, materials: ["lona"]);
        var category = await AddCategory("Accesorios");
        var supplier = await AddSupplier("Proveedor Cinco", "cinco@ameli.test");
        var safe = await AddProduct("Arnés lona", ProductSpecies.Dog, "lona", category, supplier, 8);
        await AddProduct("Arnés cuero", ProductSpecies.Dog, "cuero", category, supplier, 8);

        var before = await Service(s => s.RecommendationsAsync(actor, new RecommendationQuery { PetId = petId }, default));
        Assert.Contains(before.Items, x => x.ProductId == safe);

        await f.Db(async db =>
        {
            var pet = await db.Pets.SingleAsync(x => x.Id == petId);
            pet.Allergies = "cuero";
            pet.AllergiesDeclared = true;
            pet.UpdatedAtUtc = f.Clock.GetUtcNow().AddMinutes(1);
            pet.Revision = Guid.NewGuid();
            await db.SaveChangesAsync();
        });

        var after = await Service(s => s.RecommendationsAsync(actor, new RecommendationQuery { PetId = petId }, default));
        Assert.DoesNotContain(after.Items, x => x.Name.Contains("cuero", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(after.Items, x => x.ProductId == safe);
    }

    [Fact]
    public async Task IAR003_OutOfStockExplanationMarksUnavailableAndOffersAlternatives()
    {
        var actor = Actor(await f.Login("cliente@ameli.test"));
        var petId = await AddPet(actor.UserId, allergies: "", allergiesDeclared: true);
        var category = await AddCategory("Accesorios");
        var supplier = await AddSupplier("Proveedor Seis", "seis@ameli.test");
        var target = await AddProduct("Arnés objetivo", ProductSpecies.Dog, "algodon", category, supplier, 3);
        await AddProduct("Arnés alternativo", ProductSpecies.Dog, "algodon", category, supplier, 5);

        var list = await Service(s => s.RecommendationsAsync(actor, new RecommendationQuery { PetId = petId }, default));
        var item = Assert.Single(list.Items, x => x.ProductId == target);

        await f.Db(async db =>
        {
            var product = await db.BaseProducts.SingleAsync(x => x.Id == target);
            product.StockQuantity = 0;
            product.UpdatedAtUtc = f.Clock.GetUtcNow().AddMinutes(1);
            await db.SaveChangesAsync();
        });

        var detail = await Service(s => s.ExplanationAsync(actor, new RecommendationExplanationQuery
        {
            PetId = petId,
            ProductId = item.ProductId,
            ContextToken = item.ContextToken
        }, default));

        Assert.True(detail.IsStale);
        Assert.True(detail.IsUnavailable);
        Assert.NotEmpty(detail.Alternatives);
    }

    [Fact]
    public async Task IAR003_FallbackReturnsSafeAlternativesWhenCalculationFails()
    {
        var actor = Actor(await f.Login("cliente@ameli.test"));
        var petId = await AddPet(actor.UserId, allergies: "", allergiesDeclared: true);
        var category = await AddCategory("Accesorios");
        var supplier = await AddSupplier("Proveedor Siete", "siete@ameli.test");
        var id = await AddProduct("Arnés roto", ProductSpecies.Dog, "algodon", category, supplier, 4);
        await f.Db(async db =>
        {
            var product = await db.BaseProducts.SingleAsync(x => x.Id == id);
            product.ColorsJson = "{ json-malformado";
            await db.SaveChangesAsync();
        });

        var result = await Service(s => s.RecommendationsAsync(actor, new RecommendationQuery { PetId = petId }, default));

        Assert.Equal("fallback", result.Status);
        Assert.NotEmpty(result.Alternatives);
    }

    [Fact]
    public async Task IAR004_UsefulFeedbackIsStoredAndDuplicatesAreUpdated()
    {
        var token = await f.Login("cliente@ameli.test");
        var actor = Actor(token);
        var petId = await AddPet(actor.UserId, allergies: "", allergiesDeclared: true);
        var category = await AddCategory("Accesorios");
        var supplier = await AddSupplier("Proveedor Ocho", "ocho@ameli.test");
        var productId = await AddProduct("Arnés valorado", ProductSpecies.Dog, "algodon", category, supplier, 5);

        var first = await Service(s => s.SaveFeedbackAsync(actor, new RecommendationFeedbackRequest
        {
            PetId = petId,
            ProductId = productId,
            Value = RecommendationFeedbackValue.Useful
        }, default));

        var second = await Service(s => s.SaveFeedbackAsync(actor, new RecommendationFeedbackRequest
        {
            PetId = petId,
            ProductId = productId,
            Value = RecommendationFeedbackValue.NotInterested,
            ReasonCode = "style",
            ReasonText = "No coincide"
        }, default));

        Assert.False(first.ReplacedExisting);
        Assert.True(second.ReplacedExisting);
        Assert.Equal(1, await f.Db(db => db.RecommendationFeedback.CountAsync(x => x.OwnerUserId == actor.UserId && x.PetId == petId && x.ProductId == productId && x.SessionId == actor.SessionId)));
    }

    [Fact]
    public async Task IAR004_AnonymousCannotSaveFeedback()
    {
        using var http = f.Http();
        var response = await http.PostAsJsonAsync("/api/v1/customer/recommendations/feedback", new RecommendationFeedbackRequest
        {
            PetId = Guid.NewGuid(),
            ProductId = Guid.NewGuid(),
            Value = RecommendationFeedbackValue.Useful
        });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private async Task<T> Service<T>(Func<RecommendationService, Task<T>> action)
    {
        using var scope = f.Factory.Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<RecommendationService>());
    }

    private Task<Guid> AddCategory(string name) => f.Db(async db =>
    {
        var category = new ProductCategory { Name = name, NormalizedName = SecurityService.NormalizeEmail(name), Description = name, IsActive = true, CreatedAtUtc = f.Clock.GetUtcNow(), UpdatedAtUtc = f.Clock.GetUtcNow() };
        db.Categories.Add(category);
        await db.SaveChangesAsync();
        return category.Id;
    });

    private Task<Guid> AddSupplier(string name, string email) => f.Db(async db =>
    {
        var supplier = new Supplier { Name = name, NormalizedName = SecurityService.NormalizeEmail(name), ContactName = "", Email = email, NormalizedEmail = SecurityService.NormalizeEmail(email), Phone = "88888888", Notes = "", IsActive = true, CreatedAtUtc = f.Clock.GetUtcNow(), UpdatedAtUtc = f.Clock.GetUtcNow() };
        db.Suppliers.Add(supplier);
        await db.SaveChangesAsync();
        return supplier.Id;
    });

    private Task<Guid> AddProduct(string name, string species, string material, Guid categoryId, Guid supplierId, int stock) => f.Db(async db =>
    {
        var product = new BaseProduct
        {
            Name = name,
            NormalizedName = SecurityService.NormalizeEmail(name),
            Description = name,
            CategoryId = categoryId,
            SupplierId = supplierId,
            Species = species,
            Material = material,
            Icon = "🎀",
            UnitPrice = 10000,
            StockQuantity = stock,
            IsActive = true,
            ColorsJson = JsonSerializer.Serialize(new[] { "lavanda", "azul" }),
            GarmentType = "arnes",
            Style = "confort",
            CreatedAtUtc = f.Clock.GetUtcNow(),
            UpdatedAtUtc = f.Clock.GetUtcNow()
        };
        db.BaseProducts.Add(product);
        db.ProductSizes.Add(new ProductSizeRule
        {
            ProductId = product.Id,
            Name = "M",
            MinHeightCm = 25,
            MaxHeightCm = 45,
            MinWeightKg = 4,
            MaxWeightKg = 12,
            Equivalent = "M"
        });
        await db.SaveChangesAsync();
        return product.Id;
    });

    private Task<Guid> AddPet(Guid ownerId, decimal height = 35, decimal weight = 8, string allergies = "", bool allergiesDeclared = true,
        string[]? colors = null, string[]? materials = null, string[]? garments = null, string[]? styles = null) => f.Db(async db =>
    {
        var pet = new PetProfile
        {
            OwnerUserId = ownerId,
            Name = "Milo",
            Species = ProductSpecies.Dog,
            Breed = "Mestizo",
            AgeYears = 4,
            HeightCm = height,
            WeightKg = weight,
            Allergies = allergies,
            AllergiesDeclared = allergiesDeclared,
            IsActive = true,
            ColorsJson = JsonSerializer.Serialize(colors ?? []),
            MaterialsJson = JsonSerializer.Serialize(materials ?? []),
            GarmentTypesJson = JsonSerializer.Serialize(garments ?? []),
            StylesJson = JsonSerializer.Serialize(styles ?? []),
            PreferencesUpdatedAtUtc = f.Clock.GetUtcNow(),
            CreatedAtUtc = f.Clock.GetUtcNow(),
            UpdatedAtUtc = f.Clock.GetUtcNow()
        };
        db.Pets.Add(pet);
        await db.SaveChangesAsync();
        return pet.Id;
    });
}
