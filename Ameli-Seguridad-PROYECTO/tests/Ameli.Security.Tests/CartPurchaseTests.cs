using System.Net;
using System.Net.Http.Json;
using Ameli.Api.Domain;
using Ameli.Contracts;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Ameli.Security.Tests;

public sealed class CartPurchaseTests(SecurityFixture f) : IClassFixture<SecurityFixture>, IAsyncLifetime
{
    public Task InitializeAsync() => f.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task ManualPurchaseRequiresConsentAndRetriesDoNotDuplicateCartOrOrder()
    {
        var productId = await Product();
        using var http = f.Http(await f.Login());
        var command = new AddCartRequest { RequestId = Guid.NewGuid(), ProductId = productId, Quantity = 2 };
        var missingConsent = await http.PostAsJsonAsync("/api/v1/assistant/cart", command);
        Assert.Equal(HttpStatusCode.BadRequest, missingConsent.StatusCode);
        Assert.Equal("size_confirmation_required", (await missingConsent.Content.ReadFromJsonAsync<ApiError>())!.Code);
        Assert.Empty((await http.GetFromJsonAsync<CartView>("/api/v1/assistant/cart"))!.Lines);

        command.AcknowledgesUnspecifiedSize = true;
        Assert.Equal(HttpStatusCode.OK, (await http.PostAsJsonAsync("/api/v1/assistant/cart", command)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await http.PostAsJsonAsync("/api/v1/assistant/cart", command)).StatusCode);
        var line = (await http.GetFromJsonAsync<CartView>("/api/v1/assistant/cart"))!.Lines.Single();
        Assert.Equal(2, line.Quantity); Assert.Equal("", line.Size); Assert.Null(line.PetId);
        Assert.Equal(3, await f.Db(db => db.BaseProducts.Where(p => p.Id == productId).Select(p => p.StockQuantity).SingleAsync()));
        command.AcknowledgesUnspecifiedSize = false;
        Assert.Equal(HttpStatusCode.Conflict, (await http.PostAsJsonAsync("/api/v1/assistant/cart", command)).StatusCode);

        var checkout = new CheckoutRequest { RequestId = Guid.NewGuid(), AddressId = await Address() };
        var first = await http.PostAsJsonAsync("/api/v1/assistant/checkout", checkout);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var order = (await first.Content.ReadFromJsonAsync<OrderView>())!;
        var replay = await http.PostAsJsonAsync("/api/v1/assistant/checkout", checkout);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(order.Id, (await replay.Content.ReadFromJsonAsync<OrderView>())!.Id);
        Assert.Equal(2500m, order.Total); Assert.Equal("", order.Items.Single().Size);
        Assert.Equal(1, await f.Db(db => db.Orders.CountAsync()));
        Assert.Equal(1, await f.Db(db => db.BaseProducts.Where(p => p.Id == productId).Select(p => p.StockQuantity).SingleAsync()));
        Assert.Empty((await http.GetFromJsonAsync<CartView>("/api/v1/assistant/cart"))!.Lines);
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("", true)]
    [InlineData("Inventada", true)]
    public async Task PublishedSizesCannotBeBypassedByUnspecifiedSizeConsent(string size, bool consent)
    {
        var productId = await Product(withSizes: true);
        using var http = f.Http(await f.Login());
        var response = await http.PostAsJsonAsync("/api/v1/assistant/cart", new AddCartRequest
        { RequestId = Guid.NewGuid(), ProductId = productId, Size = size, AcknowledgesUnspecifiedSize = consent });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("invalid_size", (await response.Content.ReadFromJsonAsync<ApiError>())!.Code);
        Assert.Empty((await http.GetFromJsonAsync<CartView>("/api/v1/assistant/cart"))!.Lines);
        Assert.Equal(HttpStatusCode.OK, (await http.PostAsJsonAsync("/api/v1/assistant/cart", new AddCartRequest
        { RequestId = Guid.NewGuid(), ProductId = productId, Size = "S" })).StatusCode);
    }

    [Theory]
    [InlineData(0, HttpStatusCode.BadRequest)]
    [InlineData(21, HttpStatusCode.BadRequest)]
    [InlineData(4, HttpStatusCode.Conflict)]
    public async Task ManualPurchaseStillEnforcesQuantityAndStock(int quantity, HttpStatusCode expected)
    {
        var productId = await Product();
        using var http = f.Http(await f.Login());
        var response = await http.PostAsJsonAsync("/api/v1/assistant/cart", new AddCartRequest
        { RequestId = Guid.NewGuid(), ProductId = productId, Quantity = quantity, AcknowledgesUnspecifiedSize = true });
        Assert.Equal(expected, response.StatusCode);
        Assert.Empty((await http.GetFromJsonAsync<CartView>("/api/v1/assistant/cart"))!.Lines);
    }

    [Fact]
    public async Task UnspecifiedSizeCannotBeUsedForAPetRecommendation()
    {
        var productId = await Product();
        var petId = await Pet(f.Client);
        using var http = f.Http(await f.Login());
        var response = await http.PostAsJsonAsync("/api/v1/assistant/cart", new AddCartRequest
        { RequestId = Guid.NewGuid(), ProductId = productId, PetId = petId, AcknowledgesUnspecifiedSize = true });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("invalid_size", (await response.Content.ReadFromJsonAsync<ApiError>())!.Code);
    }

    [Theory]
    [InlineData("allergy", HttpStatusCode.BadRequest)]
    [InlineData("other_owner", HttpStatusCode.NotFound)]
    [InlineData("ambiguous", HttpStatusCode.Conflict)]
    public async Task PetPurchaseRevalidatesAllergiesOwnershipAndSize(string condition, HttpStatusCode expected)
    {
        var productId = await Product(withSizes: true);
        var owner = condition == "other_owner" ? await f.AddUser("otra@example.test", Roles.Client) : f.Client;
        var petId = await Pet(owner, condition == "allergy" ? "Algodón" : "");
        if (condition == "ambiguous") await f.Db(async db =>
        {
            db.ProductSizes.Add(new ProductSizeRule { ProductId = productId, Name = "M", MinHeightCm = 25,
                MaxHeightCm = 45, MinWeightKg = 5, MaxWeightKg = 12 });
            await db.SaveChangesAsync();
        });
        using var http = f.Http(await f.Login());
        var response = await http.PostAsJsonAsync("/api/v1/assistant/cart", new AddCartRequest
        { RequestId = Guid.NewGuid(), ProductId = productId, PetId = petId, Size = "S", AcknowledgesUnspecifiedSize = true });
        Assert.Equal(expected, response.StatusCode);
        Assert.Empty((await http.GetFromJsonAsync<CartView>("/api/v1/assistant/cart"))!.Lines);
    }

    [Theory]
    [InlineData("table", "invalid_size", HttpStatusCode.BadRequest)]
    [InlineData("stock", "insufficient_stock", HttpStatusCode.Conflict)]
    [InlineData("inactive", "product_not_found", HttpStatusCode.NotFound)]
    public async Task CheckoutRejectsProductsThatChangedAfterTheyWereAdded(string change, string code, HttpStatusCode expected)
    {
        var productId = await Product();
        using var http = f.Http(await f.Login());
        Assert.Equal(HttpStatusCode.OK, (await http.PostAsJsonAsync("/api/v1/assistant/cart", new AddCartRequest
        { RequestId = Guid.NewGuid(), ProductId = productId, AcknowledgesUnspecifiedSize = true })).StatusCode);
        await f.Db(async db =>
        {
            var product = await db.BaseProducts.SingleAsync(p => p.Id == productId);
            if (change == "table") db.ProductSizes.Add(new ProductSizeRule { ProductId = productId, Name = "S",
                MinHeightCm = 20, MaxHeightCm = 40, MinWeightKg = 2, MaxWeightKg = 10 });
            if (change == "stock") product.StockQuantity = 0;
            if (change == "inactive") product.IsActive = false;
            await db.SaveChangesAsync();
        });
        var response = await http.PostAsJsonAsync("/api/v1/assistant/checkout", new CheckoutRequest
        { RequestId = Guid.NewGuid(), AddressId = await Address() });
        Assert.Equal(expected, response.StatusCode);
        Assert.Equal(code, (await response.Content.ReadFromJsonAsync<ApiError>())!.Code);
        Assert.Equal(0, await f.Db(db => db.Orders.CountAsync()));
        Assert.Single((await http.GetFromJsonAsync<CartView>("/api/v1/assistant/cart"))!.Lines);
    }

    [Fact]
    public async Task OnlyAuthenticatedClientsCanAddProducts()
    {
        var command = new AddCartRequest { RequestId = Guid.NewGuid(), ProductId = await Product(), AcknowledgesUnspecifiedSize = true };
        using var anonymous = f.Http();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("/api/v1/assistant/cart", command)).StatusCode);
        using var staff = f.Http(await f.Login("admin@ameli.test"));
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.PostAsJsonAsync("/api/v1/assistant/cart", command)).StatusCode);
    }

    private Task<Guid> Product(bool withSizes = false) => f.Db(async db =>
    {
        var category = new ProductCategory { Name = "Accesorios", NormalizedName = "ACCESORIOS", IsActive = true };
        var supplier = new Supplier { Name = "Taller", NormalizedName = "TALLER", Email = "taller@example.test",
            NormalizedEmail = "TALLER@EXAMPLE.TEST", IsActive = true };
        var product = new BaseProduct { Name = "Accesorio", NormalizedName = "ACCESORIO", Sku = "AML-CART", CategoryId = category.Id,
            SupplierId = supplier.Id, Species = ProductSpecies.Dog, Material = "Algodón", StockQuantity = 3, UnitPrice = 1250, IsActive = true };
        db.Categories.Add(category); db.Suppliers.Add(supplier); db.BaseProducts.Add(product);
        if (withSizes) db.ProductSizes.Add(new ProductSizeRule { ProductId = product.Id, Name = "S",
            MinHeightCm = 20, MaxHeightCm = 40, MinWeightKg = 2, MaxWeightKg = 10 });
        await db.SaveChangesAsync(); return product.Id;
    });
    private Task<Guid> Pet(Guid owner, string allergies = "") => f.Db(async db =>
    {
        var pet = new PetProfile { OwnerUserId = owner, Name = "Luna", Species = ProductSpecies.Dog, Breed = "Mestiza",
            AgeYears = 2, HeightCm = 30, WeightKg = 8, AllergiesDeclared = true, Allergies = allergies, IsActive = true };
        db.Pets.Add(pet); await db.SaveChangesAsync(); return pet.Id;
    });
    private Task<Guid> Address() => f.Db(async db =>
    {
        var address = new DeliveryAddress { OwnerUserId = f.Client, Province = "San José", Canton = "San José", District = "Carmen",
            ExactAddress = "Casa azul", Reference = "Frente al parque", IsDefault = true };
        db.DeliveryAddresses.Add(address); await db.SaveChangesAsync(); return address.Id;
    });
}
