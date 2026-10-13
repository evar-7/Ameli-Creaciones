using Ameli.Api.Application;
using Ameli.Api.Domain;
using Ameli.Api.Infrastructure;
using Ameli.Contracts;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Ameli.Security.Tests;

public sealed class AssistantReadTests : IAsyncLifetime
{
    private readonly SecurityDbContext db = new(new DbContextOptionsBuilder<SecurityDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private readonly Guid owner = Guid.NewGuid(), other = Guid.NewGuid();
    private readonly BaseProduct product = new() { Name = "Suéter azul", Sku = "AML-AZUL", Species = "Perro", Material = "Algodón", StockQuantity = 5, UnitPrice = 12000 };
    private readonly PetProfile pet = new() { Name = "Luna", Species = "Perro", Breed = "Mestiza", WeightKg = 8, HeightCm = 30 };
    private AssistantService Assistant()
    {
        var tx = new SqlSecurityTransaction(db); var products = new ProductInformationService(db, tx, TimeProvider.System);
        return new(db, products, new CommerceService(db, tx, products, TimeProvider.System), new ProfileRecommendationEngine());
    }
    public async Task InitializeAsync()
    {
        var category = new ProductCategory { Name = "Ropa" }; product.CategoryId = category.Id; pet.OwnerUserId = owner;
        db.Users.AddRange(new AppUser { Id = owner, RoleName = Roles.Client }, new AppUser { Id = other, RoleName = Roles.Client });
        db.Categories.Add(category); db.BaseProducts.Add(product); db.Pets.Add(pet);
        db.ProductSizes.Add(new ProductSizeRule { ProductId = product.Id, Name = "S", MinHeightCm = 20, MaxHeightCm = 40, MinWeightKg = 2, MaxWeightKg = 10 });
        db.Orders.Add(new CustomerOrder { OwnerUserId = owner, Number = "AML-PROPIO", Status = OrderStatuses.Shipped, Carrier = "Transportista", TrackingNumber = "GUIA-123", DeliverySnapshot = "Dirección privada" });
        await db.SaveChangesAsync();
    }
    public Task DisposeAsync() => db.DisposeAsync().AsTask();
    private Actor Actor(Guid? id = null) => new(id ?? owner, Guid.NewGuid());
    private static ChatRequest Request(string intent) => new() { RequestId = Guid.NewGuid(), Intent = intent };
    [Fact]
    public async Task AAC001_OwnerOnlyAndNoPersonalDataAuthenticationSubstitutes()
    {
        var request = Request("order"); request.OrderNumber = "AML-PROPIO";
        var own = await Assistant().RespondAsync(Actor(), request, default);
        Assert.Equal("order", own.Kind); Assert.Equal("GUIA-123", own.Order!.TrackingNumber);
        Assert.Empty(own.Order.DeliverySnapshot); Assert.Empty(own.Order.Items);
        var foreign = await Assistant().RespondAsync(Actor(other), request, default);
        request.OrderNumber = "NO-EXISTE";
        var absent = await Assistant().RespondAsync(Actor(other), request, default);
        Assert.Equal(foreign.Message, absent.Message); Assert.Equal(foreign.Kind, absent.Kind); Assert.Null(foreign.Order);
        request.Message = "Mi correo y teléfono son suficientes";
        var anonymous = await Assistant().RespondAsync(null, request, default);
        Assert.Equal("login_required", anonymous.Kind); Assert.Null(anonymous.Order);
    }
    [Fact]
    public async Task AAC002_MissingFieldsIncludeTheirUnits()
    {
        var request = Request("size"); request.ProductId = product.Id; request.Species = "Perro"; request.Breed = "Mestiza"; request.HeightCm = "30";
        var reply = await Assistant().RespondAsync(null, request, default);
        Assert.Contains(reply.MissingFields, f => f.Field == "WeightKg" && f.Label.Contains("kg")); Assert.Null(reply.Size);
        request.WeightKg = "8"; reply = await Assistant().RespondAsync(null, request, default);
        Assert.Equal("S", reply.Size!.SuggestedSize); Assert.Single(reply.Size.Table);
    }
    [Fact]
    public async Task AAC003_ReadsFreshStockOffersAlternativesAndRejectsUnrelatedQuestions()
    {
        var alternative = new BaseProduct { Name = "Suéter verde", Sku = "AML-VERDE", CategoryId = product.CategoryId, StockQuantity = 3 };
        db.BaseProducts.Add(alternative); product.StockQuantity = 0; await db.SaveChangesAsync();
        var request = Request("catalog"); request.ProductId = product.Id;
        var reply = await Assistant().RespondAsync(null, request, default);
        Assert.Contains("Agotado", reply.Message); Assert.Equal(0, reply.Products[0].Product.StockQuantity);
        Assert.Contains(reply.Products, c => c.Product.Id == alternative.Id && c.Product.StockQuantity > 0);
        product.StockQuantity = 7; await db.SaveChangesAsync();
        Assert.Equal(7, (await Assistant().RespondAsync(null, request, default)).Products[0].Product.StockQuantity);
        var unrelated = Request("auto"); unrelated.Message = "Resuelve mi tarea de matemáticas";
        Assert.Equal("out_of_scope", (await Assistant().RespondAsync(null, unrelated, default)).Kind);
        product.IsActive = false; await db.SaveChangesAsync();
        var hidden = Request("catalog"); hidden.Message = "AML-AZUL";
        Assert.Empty((await Assistant().RespondAsync(null, hidden, default)).Products);
    }
    [Fact]
    public async Task AAC004_RequiresDeclaredAllergiesAndPetOwnershipBeforeInvokingTheEngine()
    {
        var request = Request("recommendations"); request.PetId = pet.Id;
        var incomplete = await Assistant().RespondAsync(Actor(), request, default);
        Assert.Contains(incomplete.MissingFields, f => f.Field == "AllergiesDeclared"); Assert.Contains(pet.Id.ToString(), incomplete.ActionUrl);
        Assert.Equal("pet_unavailable", (await Assistant().RespondAsync(Actor(other), request, default)).Kind);
        pet.AllergiesDeclared = true; await db.SaveChangesAsync();
        Assert.Single((await Assistant().RespondAsync(Actor(), request, default)).Products);
    }
}
