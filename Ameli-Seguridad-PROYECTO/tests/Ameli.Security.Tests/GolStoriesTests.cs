using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ameli.Api.Application;
using Ameli.Api.Domain;
using Ameli.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Ameli.Security.Tests;

public sealed class GolStoriesTests(SecurityFixture fixture) : IClassFixture<SecurityFixture>, IAsyncLifetime
{
    private const string Pixel = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jRZcAAAAASUVORK5CYII=";
    public Task InitializeAsync() => fixture.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<Guid> Product(int stock = 5)
    {
        return await fixture.Db(async db =>
        {
            var key = Guid.NewGuid().ToString("N")[..8];
            var category = new ProductCategory { Name = "Ropa " + key, NormalizedName = "ROPA " + key };
            var supplier = new Supplier { Name = "Taller " + key, NormalizedName = "TALLER " + key, Email = key + "@example.test", NormalizedEmail = key + "@EXAMPLE.TEST" };
            var product = new BaseProduct
            {
                Name = "Suéter " + key, NormalizedName = "SUETER " + key, CategoryId = category.Id, SupplierId = supplier.Id,
                Species = "Perro", Material = "Algodón", StockQuantity = stock, UnitPrice = 1250m
            };
            db.Categories.Add(category); db.Suppliers.Add(supplier); db.BaseProducts.Add(product);
            await db.SaveChangesAsync();
            return product.Id;
        });
    }

    private async Task<Guid> Address(HttpClient http)
    {
        using var response = await http.PostAsJsonAsync("/api/v1/customer/addresses", new DeliveryAddressRequest
        { Province = "San José", Canton = "San José", District = "Carmen", ExactAddress = "Casa azul", Reference = "Frente al parque" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<DeliveryAddressView>())!.Id;
    }

    private static CheckoutRequest Payment(Guid addressId, string number = "4242424242424242") => new()
    {
        RequestId = Guid.NewGuid(), AddressId = addressId, CardNumber = number,
        CardholderName = "Cliente de prueba", ExpirationDate = "12/39", Cvc = "123"
    };

    private static async Task Add(HttpClient http, Guid productId, int quantity = 1)
    {
        using var response = await http.PostAsJsonAsync("/api/v1/assistant/cart", new AddCartRequest
        { RequestId = Guid.NewGuid(), ProductId = productId, Quantity = quantity, AcknowledgesUnspecifiedSize = true });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GOL002_StockErrorsUseTheRequestedMessages()
    {
        using var http = fixture.Http(await fixture.Login());
        using var empty = await http.PostAsJsonAsync("/api/v1/assistant/cart", new AddCartRequest
        { RequestId = Guid.NewGuid(), ProductId = await Product(0), AcknowledgesUnspecifiedSize = true });
        Assert.Equal(HttpStatusCode.Conflict, empty.StatusCode);
        Assert.Equal("Producto agotado", (await empty.Content.ReadFromJsonAsync<ApiError>())!.Message);
        using var excess = await http.PostAsJsonAsync("/api/v1/assistant/cart", new AddCartRequest
        { RequestId = Guid.NewGuid(), ProductId = await Product(2), Quantity = 3, AcknowledgesUnspecifiedSize = true });
        Assert.Equal(HttpStatusCode.Conflict, excess.StatusCode);
        Assert.Equal("Existencias de artículo superadas", (await excess.Content.ReadFromJsonAsync<ApiError>())!.Message);
        Assert.Empty((await http.GetFromJsonAsync<CartView>("/api/v1/assistant/cart"))!.Lines);
        await Add(http, await Product(30), 21);
        Assert.Equal(21, (await http.GetFromJsonAsync<CartView>("/api/v1/assistant/cart"))!.Lines.Single().Quantity);
    }

    [Fact]
    public async Task GOL002And003_UpdatesRecalculateEveryAmountAndPreserveTheLine()
    {
        var options = fixture.Factory.Services.GetRequiredService<IOptions<CommerceOptions>>().Value;
        options.DiscountPercent = 10; options.TaxPercent = 13;
        try
        {
            using var http = fixture.Http(await fixture.Login());
            var product = await Product();
            using var added = await http.PostAsJsonAsync("/api/v1/assistant/cart", new AddCartRequest
            { RequestId = Guid.NewGuid(), ProductId = product, Quantity = 2, AcknowledgesUnspecifiedSize = true });
            var initial = (await added.Content.ReadFromJsonAsync<CartAddResult>())!.Cart!;
            Assert.Equal(2500m, initial.Subtotal); Assert.Equal(250m, initial.Discount); Assert.Equal(292.50m, initial.Tax);
            Assert.Equal(2542.50m, initial.Total);
            var line = initial.Lines.Single();
            using var updated = await http.PutAsJsonAsync($"/api/v1/assistant/cart/{line.Id}", new UpdateCartRequest { Quantity = 3, Revision = line.Revision });
            Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
            var cart = (await updated.Content.ReadFromJsonAsync<CartView>())!;
            Assert.Equal(line.Id, cart.Lines.Single().Id); Assert.Equal(3, cart.Lines.Single().Quantity);
            Assert.Equal(3750m, cart.Subtotal); Assert.Equal(375m, cart.Discount); Assert.Equal(438.75m, cart.Tax); Assert.Equal(3813.75m, cart.Total);
            using var invalid = await http.PutAsJsonAsync($"/api/v1/assistant/cart/{line.Id}", new { quantity = 1.5, revision = cart.Lines.Single().Revision });
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
            Assert.Equal(3, (await http.GetFromJsonAsync<CartView>("/api/v1/assistant/cart"))!.Lines.Single().Quantity);
            using var removed = await http.DeleteAsync($"/api/v1/assistant/cart/{line.Id}");
            Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
            var empty = (await http.GetFromJsonAsync<CartView>("/api/v1/assistant/cart"))!;
            Assert.Empty(empty.Lines); Assert.Equal(0, empty.Total); Assert.False(empty.CanCheckout);
        }
        finally { options.DiscountPercent = 0; options.TaxPercent = 0; }
    }

    [Fact]
    public async Task GOL003_ChangedStockBlocksCheckoutUntilTheQuantityIsConfirmed()
    {
        using var http = fixture.Http(await fixture.Login());
        var id = await Product(3); await Add(http, id, 3);
        await fixture.Db(async db => { (await db.BaseProducts.SingleAsync(item => item.Id == id)).StockQuantity = 1; await db.SaveChangesAsync(); });
        var cart = (await http.GetFromJsonAsync<CartView>("/api/v1/assistant/cart"))!;
        var line = cart.Lines.Single();
        Assert.False(cart.CanCheckout); Assert.NotNull(line.Issue); Assert.Equal(1, line.MaximumQuantity);
        var payment = Payment(await Address(http));
        using var blocked = await http.PostAsJsonAsync("/api/v1/assistant/checkout", payment);
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
        Assert.Equal(0, await fixture.Db(db => db.PaymentAttempts.CountAsync()));
        using var confirmed = await http.PutAsJsonAsync($"/api/v1/assistant/cart/{line.Id}", new UpdateCartRequest { Quantity = 1, Revision = line.Revision });
        var updated = (await confirmed.Content.ReadFromJsonAsync<CartView>())!;
        Assert.True(updated.CanCheckout); Assert.Equal(line.Id, updated.Lines.Single().Id);
        payment.CartRevision = updated.Revision;
        using var order = await http.PostAsJsonAsync("/api/v1/assistant/checkout", payment);
        Assert.Equal(HttpStatusCode.OK, order.StatusCode);
        Assert.Equal(0, await fixture.Db(db => db.BaseProducts.Where(item => item.Id == id).Select(item => item.StockQuantity).SingleAsync()));
    }

    [Fact]
    public async Task GOL004_MissingAddressAndDeclinedPaymentNeverCreateAnOrderOrReduceStock()
    {
        using var http = fixture.Http(await fixture.Login());
        var product = await Product(); await Add(http, product, 2);
        using var missing = await http.PostAsJsonAsync("/api/v1/assistant/checkout", Payment(Guid.Empty));
        Assert.Equal("Dirección de envío no especificada", (await missing.Content.ReadFromJsonAsync<ApiError>())!.Message);
        var payment = Payment(await Address(http), "4000000000000002");
        using var declined = await http.PostAsJsonAsync("/api/v1/assistant/checkout", payment);
        Assert.Equal(HttpStatusCode.PaymentRequired, declined.StatusCode);
        Assert.Equal("Pago rechazado, intenta con otra tarjeta", (await declined.Content.ReadFromJsonAsync<ApiError>())!.Message);
        using var replay = await http.PostAsJsonAsync("/api/v1/assistant/checkout", payment);
        Assert.Equal(HttpStatusCode.PaymentRequired, replay.StatusCode);
        Assert.Equal(0, await fixture.Db(db => db.Orders.CountAsync()));
        Assert.Equal(5, await fixture.Db(db => db.BaseProducts.Where(item => item.Id == product).Select(item => item.StockQuantity).SingleAsync()));
        Assert.Equal(1, await fixture.Db(db => db.PaymentAttempts.CountAsync()));
        var persisted = await fixture.Db(async db => JsonSerializer.Serialize(await db.PaymentAttempts.AsNoTracking().ToListAsync()) + JsonSerializer.Serialize(await db.Events.AsNoTracking().ToListAsync()));
        Assert.DoesNotContain(payment.CardNumber, persisted); Assert.DoesNotContain("Cvc", persisted);
        Assert.True(await fixture.Db(db => db.Events.AnyAsync(item => item.Action == "payment_declined" && item.Outcome == "Fallido")));
        using var approved = await http.PostAsJsonAsync("/api/v1/assistant/checkout", Payment(payment.AddressId));
        Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
        Assert.Equal(2, await fixture.Db(db => db.PaymentAttempts.CountAsync()));
    }

    [Fact]
    public async Task GOL004And005_ConfirmedOrdersKeepSnapshotsAndChronologicalTracking()
    {
        using var http = fixture.Http(await fixture.Login());
        Assert.Empty((await http.GetFromJsonAsync<List<OrderView>>("/api/v1/assistant/orders"))!);
        var product = await Product(); await Add(http, product, 2);
        var payment = Payment(await Address(http));
        using var placed = await http.PostAsJsonAsync("/api/v1/assistant/checkout", payment);
        var order = (await placed.Content.ReadFromJsonAsync<OrderView>())!;
        Assert.Equal(OrderStatuses.Confirmed, order.Status); Assert.Equal(2500m, order.Total); Assert.Contains("Casa azul", order.DeliverySnapshot);
        using var replay = await http.PostAsJsonAsync("/api/v1/assistant/checkout", payment);
        Assert.Equal(order.Id, (await replay.Content.ReadFromJsonAsync<OrderView>())!.Id);
        Assert.Equal(1, await fixture.Db(db => db.PaymentAttempts.CountAsync()));
        Assert.Equal(3, await fixture.Db(db => db.BaseProducts.Where(item => item.Id == product).Select(item => item.StockQuantity).SingleAsync()));
        using var staff = fixture.Http(await fixture.Login("logistica@ameli.test"));
        foreach (var status in new[] { OrderStatuses.Preparing, OrderStatuses.LegacyShipped, OrderStatuses.Delivered })
        {
            using var updated = await staff.PutAsJsonAsync($"/api/v1/assistant/staff/orders/{order.Id}",
                new OrderUpdateRequest { Status = status, Carrier = "Correos", TrackingNumber = "CR-123" });
            Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        }
        var tracked = (await http.GetFromJsonAsync<OrderView>($"/api/v1/assistant/orders/{order.Number}"))!;
        Assert.Equal(new[] { OrderStatuses.Confirmed, OrderStatuses.Preparing, OrderStatuses.Shipped, OrderStatuses.Delivered }, tracked.History!.Select(item => item.Status));
        Assert.All(tracked.History!, item => Assert.NotEqual(default, item.OccurredAtUtc));
        Assert.Equal("Correos", tracked.Carrier); Assert.Equal("CR-123", tracked.TrackingNumber);
        Assert.Single((await http.GetFromJsonAsync<List<OrderView>>("/api/v1/assistant/orders"))!);
        await fixture.AddUser("otro@example.test", Roles.Client);
        using var other = fixture.Http(await fixture.Login("otro@example.test"));
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/v1/assistant/orders/{order.Number}")).StatusCode);
        Assert.Empty((await other.GetFromJsonAsync<List<OrderView>>("/api/v1/assistant/orders"))!);
    }

    private static ProductUpdateRequest Edit(ProductEditView view) => new()
    {
        Name = view.Product.Name, Description = view.Product.Description, CategoryId = view.Product.CategoryId,
        SupplierId = view.Product.SupplierId, Species = view.Product.Species, Material = view.Product.Material,
        Icon = view.Product.Icon, UnitPrice = view.Product.UnitPrice, StockQuantity = view.Product.StockQuantity,
        IsActive = view.Product.IsActive, Revision = view.Revision, UnitCost = view.UnitCost, MinimumStock = view.MinimumStock,
        Sizes = view.Sizes.Select(item => new ProductSizeRequest { Name = item.Name, MinHeightCm = item.MinHeightCm,
            MaxHeightCm = item.MaxHeightCm, MinWeightKg = item.MinWeightKg, MaxWeightKg = item.MaxWeightKg, Equivalent = item.Equivalent }).ToArray()
    };

    [Fact]
    public async Task GOL004_CardFieldsAreValidatedAndConcurrentRetriesCreateOneOrder()
    {
        using var http = fixture.Http(await fixture.Login());
        var product = await Product(); await Add(http, product, 2);
        var address = await Address(http);
        var invalid = Payment(address); invalid.CardNumber = "incorrecto"; invalid.CardholderName = "";
        invalid.ExpirationDate = "01/20"; invalid.Cvc = "x";
        using var rejected = await http.PostAsJsonAsync("/api/v1/assistant/checkout", invalid);
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        var errors = (await rejected.Content.ReadFromJsonAsync<ApiError>())!.Errors!;
        foreach (var name in new[] { "CardNumber", "CardholderName", "ExpirationDate", "Cvc" }) Assert.Contains(name, errors.Keys);
        Assert.Equal(0, await fixture.Db(db => db.PaymentAttempts.CountAsync()));
        var payment = Payment(address);
        var results = await Task.WhenAll(http.PostAsJsonAsync("/api/v1/assistant/checkout", payment), http.PostAsJsonAsync("/api/v1/assistant/checkout", payment));
        Assert.All(results, result => Assert.Equal(HttpStatusCode.OK, result.StatusCode));
        var first = (await results[0].Content.ReadFromJsonAsync<OrderView>())!;
        var second = (await results[1].Content.ReadFromJsonAsync<OrderView>())!;
        Assert.Equal(first.Id, second.Id);
        foreach (var result in results) result.Dispose();
        Assert.Equal(1, await fixture.Db(db => db.Orders.CountAsync()));
        Assert.Equal(1, await fixture.Db(db => db.PaymentAttempts.CountAsync()));
        Assert.Equal(3, await fixture.Db(db => db.BaseProducts.Where(item => item.Id == product).Select(item => item.StockQuantity).SingleAsync()));
    }

    [Fact]
    public async Task GOL011_EditSavesCommercialFieldsImageAndAuditWithoutChangingConfirmedOrders()
    {
        using var client = fixture.Http(await fixture.Login());
        var id = await Product(); await Add(client, id);
        using var placed = await client.PostAsJsonAsync("/api/v1/assistant/checkout", Payment(await Address(client)));
        var order = (await placed.Content.ReadFromJsonAsync<OrderView>())!;
        using var admin = fixture.Http(await fixture.Login("admin@ameli.test"));
        var current = (await admin.GetFromJsonAsync<ProductEditView>($"/api/v1/admin/catalog/products/{id}"))!;
        var request = Edit(current);
        request.Name = "Producto actualizado"; request.Description = "Nueva descripción"; request.UnitPrice = 2200m;
        request.UnitCost = 1300m; request.Material = "Lino"; request.Species = "Gato"; request.MinimumStock = 7;
        request.ImageFileName = "producto.png"; request.ImageBase64Data = Pixel;
        request.Sizes = [new ProductSizeRequest { Name = "S", MinHeightCm = 20, MaxHeightCm = 40, MinWeightKg = 2, MaxWeightKg = 10 }];
        using var saved = await admin.PutAsJsonAsync($"/api/v1/admin/catalog/products/{id}", request);
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var updated = (await saved.Content.ReadFromJsonAsync<ProductEditView>())!;
        Assert.Equal(request.Name, updated.Product.Name); Assert.Equal(1300m, updated.UnitCost); Assert.Equal(7, updated.MinimumStock);
        Assert.Equal("Gato", updated.Product.Species); Assert.Single(updated.Sizes); Assert.NotNull(updated.ImageUrl);
        var image = (await client.GetFromJsonAsync<ProductImagePayload>($"/api/v1/assistant/products/{id}/image"))!;
        Assert.Equal(Convert.FromBase64String(Pixel), image.Content); Assert.Equal("image/png", image.ContentType);
        var snapshot = (await client.GetFromJsonAsync<OrderView>($"/api/v1/assistant/orders/{order.Number}"))!;
        Assert.Equal(1250m, snapshot.Items.Single().UnitPrice); Assert.Equal(order.Items.Single().ProductName, snapshot.Items.Single().ProductName);
        var audit = await fixture.Db(db => db.Events.SingleAsync(item => item.Action == "catalog_product_update" && item.Outcome == "Exitoso"));
        Assert.Contains("Producto actualizado", audit.AfterJson!); Assert.Contains("1250", audit.BeforeJson!); Assert.DoesNotContain(Pixel, audit.AfterJson!);
        request.Revision = updated.Revision;
        using var unchanged = await admin.PutAsJsonAsync($"/api/v1/admin/catalog/products/{id}", request);
        Assert.Equal(updated.Revision, (await unchanged.Content.ReadFromJsonAsync<ProductEditView>())!.Revision);
        Assert.Equal(1, await fixture.Db(db => db.Events.CountAsync(item => item.Action == "catalog_product_update" && item.Outcome == "Exitoso")));
    }

    [Fact]
    public async Task GOL011_InvalidStaleAndUnauthorizedEditsPreserveTheProduct()
    {
        var id = await Product();
        using var admin = fixture.Http(await fixture.Login("admin@ameli.test"));
        var current = (await admin.GetFromJsonAsync<ProductEditView>($"/api/v1/admin/catalog/products/{id}"))!;
        var request = Edit(current); request.UnitCost = -1;
        using var invalid = await admin.PutAsJsonAsync($"/api/v1/admin/catalog/products/{id}", request);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Contains("UnitCost", (await invalid.Content.ReadFromJsonAsync<ApiError>())!.Errors!.Keys);
        var preserved = (await admin.GetFromJsonAsync<ProductEditView>($"/api/v1/admin/catalog/products/{id}"))!;
        Assert.Equal(current.Revision, preserved.Revision); Assert.Equal(0, preserved.UnitCost);
        request = Edit(current); request.Revision = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PutAsJsonAsync($"/api/v1/admin/catalog/products/{id}", request)).StatusCode);
        using var logistics = fixture.Http(await fixture.Login("logistica@ameli.test"));
        Assert.Equal(HttpStatusCode.Forbidden, (await logistics.GetAsync($"/api/v1/admin/catalog/products/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await logistics.PutAsJsonAsync($"/api/v1/admin/catalog/products/{id}", Edit(current))).StatusCode);
        Assert.True(await fixture.Db(db => db.Events.AnyAsync(item => item.Action == "http_access_denied" && item.ActorRole == Roles.Logistics)));
        Assert.Equal(current.Revision, (await admin.GetFromJsonAsync<ProductEditView>($"/api/v1/admin/catalog/products/{id}"))!.Revision);
    }
}
