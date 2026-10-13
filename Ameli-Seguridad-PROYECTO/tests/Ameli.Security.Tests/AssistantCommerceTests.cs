using System.Net;
using System.Net.Http.Json;
using Ameli.Api.Domain;
using Ameli.Contracts;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static Ameli.Security.Tests.SecurityFixture;

namespace Ameli.Security.Tests;

public sealed class AssistantCommerceTests(SecurityFixture f) : IClassFixture<SecurityFixture>, IAsyncLifetime
{
    public Task InitializeAsync() => f.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;
    [Fact]
    public async Task AAC_CartAndCheckoutAreIdempotentAndOrderSnapshotsSurviveProfileChanges()
    {
        var productId = await f.Db(async db =>
        {
            var category = new ProductCategory { Name = "Ropa", NormalizedName = "ROPA" };
            var supplier = new Supplier { Name = "Taller", NormalizedName = "TALLER", Email = "taller@example.test", NormalizedEmail = "TALLER@EXAMPLE.TEST" };
            var product = new BaseProduct { Name = "Suéter", NormalizedName = "SUETER", Sku = "AML-SUETER", CategoryId = category.Id, SupplierId = supplier.Id,
                Species = "Perro", Material = "Algodón", StockQuantity = 5, UnitPrice = 10000 };
            db.Categories.Add(category); db.Suppliers.Add(supplier); db.BaseProducts.Add(product);
            db.ProductSizes.Add(new ProductSizeRule { ProductId = product.Id, Name = "S", MinHeightCm = 20, MaxHeightCm = 40, MinWeightKg = 2, MaxWeightKg = 10 });
            await db.SaveChangesAsync(); return product.Id;
        });
        using var http = f.Http(await f.Login());
        var pet = (await (await http.PostAsJsonAsync("/api/v1/customer/pets", new PetRequest { Name = "Luna", Species = "Perro", Breed = "Mestiza",
            AgeYears = "2", WeightKg = "8", HeightCm = "30", AllergiesDeclared = true })).Content.ReadFromJsonAsync<PetView>())!;
        var address = (await (await http.PostAsJsonAsync("/api/v1/customer/addresses", new DeliveryAddressRequest { Province = "San José", Canton = "San José",
            District = "Carmen", ExactAddress = "Casa azul", Reference = "Frente al parque" })).Content.ReadFromJsonAsync<DeliveryAddressView>())!;
        var command = new AddCartRequest { RequestId = Guid.NewGuid(), ProductId = productId, PetId = pet.Id, Size = "S" };
        Assert.Equal(HttpStatusCode.OK, (await http.PostAsJsonAsync("/api/v1/assistant/cart", command)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await http.PostAsJsonAsync("/api/v1/assistant/cart", command)).StatusCode);
        Assert.Equal(1, (await http.GetFromJsonAsync<CartView>("/api/v1/assistant/cart"))!.Lines.Single().Quantity);
        var checkout = new CheckoutRequest { RequestId = Guid.NewGuid(), AddressId = address.Id };
        var order = (await (await http.PostAsJsonAsync("/api/v1/assistant/checkout", checkout)).Content.ReadFromJsonAsync<OrderView>())!;
        var replay = (await (await http.PostAsJsonAsync("/api/v1/assistant/checkout", checkout)).Content.ReadFromJsonAsync<OrderView>())!;
        Assert.Equal(order.Id, replay.Id); Assert.Equal(4, await f.Db(db => db.BaseProducts.Where(p => p.Id == productId).Select(p => p.StockQuantity).SingleAsync()));
        await http.PostAsync($"/api/v1/customer/pets/{pet.Id}/deactivate", null); await http.DeleteAsync($"/api/v1/customer/addresses/{address.Id}");
        var snapshot = (await http.GetFromJsonAsync<OrderView>($"/api/v1/assistant/orders/{order.Number}"))!;
        Assert.Contains("Casa azul", snapshot.DeliverySnapshot); Assert.Contains("Luna", snapshot.Items.Single().PetSnapshot);
        await f.AddUser("otro@example.test", Roles.Client); using var other = f.Http(await f.Login("otro@example.test"));
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/v1/assistant/orders/{order.Number}")).StatusCode);
        using var staff = f.Http(await f.Login("logistica@ameli.test"));
        Assert.Equal(HttpStatusCode.OK, (await staff.PutAsJsonAsync($"/api/v1/assistant/staff/orders/{order.Id}",
            new OrderUpdateRequest { Status = OrderStatuses.Shipped, Carrier = "Correos", TrackingNumber = "CR-123" })).StatusCode);
        var reply = (await (await http.PostAsJsonAsync("/api/v1/assistant/messages", new ChatRequest { RequestId = Guid.NewGuid(), Intent = "order", OrderNumber = order.Number }))
            .Content.ReadFromJsonAsync<ChatReply>())!;
        Assert.Equal("CR-123", reply.Order!.TrackingNumber); Assert.Empty(reply.Order.DeliverySnapshot);
    }
}
