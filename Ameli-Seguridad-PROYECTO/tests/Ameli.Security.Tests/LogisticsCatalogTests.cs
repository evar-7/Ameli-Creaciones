using System.Net;
using System.Net.Http.Json;
using Ameli.Api.Application;
using Ameli.Api.Domain;
using Ameli.Contracts;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Ameli.Security.Tests;

public sealed class LogisticsCatalogTests(SecurityFixture f) : IClassFixture<SecurityFixture>, IAsyncLifetime
{
    public Task InitializeAsync() => f.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;
    private const string Catalog = "/api/v1/admin/catalog/";
    private static string Staff(Guid id) => $"/api/v1/assistant/staff/orders/{id}";
    private static OrderLineQuantity[] All(OrderView order, int? quantity = null) => order.Items.Select(x => new OrderLineQuantity { ItemId = x.Id, Quantity = quantity ?? x.Quantity }).ToArray();
    private async Task<BaseProduct> Product()
    {
        return await f.Db(async db => {
            var key = Guid.NewGuid().ToString("N")[..8];
            var category = new ProductCategory { Name = "Ropa " + key, NormalizedName = "ROPA " + key };
            var supplier = new Supplier { Name = "Taller " + key, NormalizedName = "TALLER " + key, Email = key + "@example.test", NormalizedEmail = key + "@EXAMPLE.TEST" };
            var product = new BaseProduct { Name = "Suéter " + key, NormalizedName = "SUETER " + key, CategoryId = category.Id, SupplierId = supplier.Id,
                UnitPrice = 1000, StockQuantity = 20, Species = "Perro", Material = "Algodón" };
            db.Categories.Add(category); db.Suppliers.Add(supplier); db.BaseProducts.Add(product); await db.SaveChangesAsync(); return product;
        });
    }
    private async Task<OrderView> Order(BaseProduct product)
    {
        using var buyer = f.Http(await f.Login());
        using var addressResponse = await buyer.PostAsJsonAsync("/api/v1/customer/addresses", new DeliveryAddressRequest
        { Province = "San José", Canton = "San José", District = "Carmen", ExactAddress = "Casa azul", Reference = "Frente al parque" });
        Assert.True(addressResponse.StatusCode == HttpStatusCode.OK, await addressResponse.Content.ReadAsStringAsync());
        var address = (await addressResponse.Content.ReadFromJsonAsync<DeliveryAddressView>())!;
        Assert.Equal(HttpStatusCode.OK, (await buyer.PostAsJsonAsync("/api/v1/assistant/cart", new AddCartRequest
        { RequestId = Guid.NewGuid(), ProductId = product.Id, Quantity = 2, AcknowledgesUnspecifiedSize = true })).StatusCode);
        using var response = await buyer.PostAsJsonAsync("/api/v1/assistant/checkout", new CheckoutRequest
        { RequestId = Guid.NewGuid(), AddressId = address.Id, CardNumber = "4242424242424242", ExpirationDate = "12/39", CardholderName = "Cliente Prueba", Cvc = "123" });
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync()); return (await response.Content.ReadFromJsonAsync<OrderView>())!;
    }
    private static async Task<OrderView> Ok(HttpResponseMessage response)
    { Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync()); return (await response.Content.ReadFromJsonAsync<OrderView>())!; }
    private async Task<OrderView> Shipped(HttpClient staff, OrderView order, string tracking = "CR-123")
    {
        await Ok(await staff.PostAsJsonAsync(Staff(order.Id) + "/prepare", new { }));
        await Ok(await staff.PutAsJsonAsync(Staff(order.Id) + "/packing", new PackingRequest { Items = All(order) }));
        return await Ok(await staff.PostAsJsonAsync(Staff(order.Id) + "/dispatch", new DispatchRequest { Carrier = "Correos", TrackingNumber = tracking }));
    }

    [Fact]
    public async Task GOL012_DeactivationRequiresReasonHidesPurchasesAndPreservesOrdersAndAudit()
    {
        var product = await Product(); var order = await Order(product);
        using var admin = f.Http(await f.Login("admin@ameli.test"));
        var edit = (await admin.GetFromJsonAsync<ProductEditView>(Catalog + $"products/{product.Id}"))!;
        var url = Catalog + $"products/{product.Id}/availability";
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync(url, new AvailabilityRequest { Revision = edit.Revision })).StatusCode);
        var response = await admin.PutAsJsonAsync(url, new AvailabilityRequest { Revision = edit.Revision, Reason = "Retiro temporal del catálogo" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var inactive = (await response.Content.ReadFromJsonAsync<ProductEditView>())!; Assert.False(inactive.Product.IsActive);
        using var buyer = f.Http(await f.Login());
        Assert.DoesNotContain((await buyer.GetFromJsonAsync<List<StoreProductView>>("/api/v1/assistant/products"))!, x => x.Id == product.Id);
        Assert.Equal(HttpStatusCode.NotFound, (await buyer.PostAsJsonAsync("/api/v1/assistant/cart", new AddCartRequest
        { RequestId = Guid.NewGuid(), ProductId = product.Id, AcknowledgesUnspecifiedSize = true })).StatusCode);
        var saved = (await buyer.GetFromJsonAsync<OrderView>($"/api/v1/assistant/orders/{order.Number}"))!;
        Assert.Equal(order.Items.Single().ProductName, saved.Items.Single().ProductName); Assert.Equal(order.Total, saved.Total);
        var audit = await f.Db(db => db.Events.SingleAsync(x => x.Action == "catalog_product_deactivate" && x.Outcome == "Exitoso"));
        Assert.Contains("Retiro temporal", audit.Detail); Assert.Contains("true", audit.BeforeJson); Assert.Contains("false", audit.AfterJson);
        using var staff = f.Http(await f.Login("logistica@ameli.test"));
        Assert.Equal(OrderStatuses.Shipped, (await Shipped(staff, order)).Status);
    }

    [Fact]
    public async Task GOL012_ReactivationValidatesDependenciesAndCannotBeBypassedThroughEditors()
    {
        var product = await Product(); using var admin = f.Http(await f.Login("admin@ameli.test"));
        var current = (await admin.GetFromJsonAsync<ProductEditView>(Catalog + $"products/{product.Id}"))!;
        var inactive = await admin.PutAsJsonAsync(Catalog + $"products/{product.Id}/availability", new AvailabilityRequest { Revision = current.Revision, Reason = "Retiro de prueba" });
        current = (await inactive.Content.ReadFromJsonAsync<ProductEditView>())!;
        await f.Db(db => db.Suppliers.Where(x => x.Id == product.SupplierId).ExecuteUpdateAsync(x => x.SetProperty(s => s.IsActive, false)));
        var bad = await admin.PutAsJsonAsync(Catalog + $"products/{product.Id}/availability", new AvailabilityRequest { Revision = current.Revision, IsActive = true });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode); Assert.Contains("SupplierId", (await bad.Content.ReadFromJsonAsync<ApiError>())!.Errors!.Keys);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync($"/api/v1/assistant/admin/products/{product.Id}", new ProductPresentationRequest
        { Sku = product.Sku, Material = product.Material, UnitPrice = 1000, StockQuantity = 20, IsActive = true })).StatusCode);
        await f.Db(db => db.Suppliers.Where(x => x.Id == product.SupplierId).ExecuteUpdateAsync(x => x.SetProperty(s => s.IsActive, true)));
        var active = await admin.PutAsJsonAsync(Catalog + $"products/{product.Id}/availability", new AvailabilityRequest { Revision = current.Revision, IsActive = true });
        Assert.True((await active.Content.ReadFromJsonAsync<ProductEditView>())!.Product.IsActive);
        Assert.True(await f.Db(db => db.Events.AnyAsync(x => x.Action == "catalog_product_reactivate")));
    }

    [Fact]
    public async Task GOL015_EditPreservesAssociationsAuditsAndRejectsDuplicateStaleAndInvalidValues()
    {
        var product = await Product(); var second = await Product(); using var admin = f.Http(await f.Login("admin@ameli.test"));
        var url = Catalog + $"categories/{product.CategoryId}";
        var category = (await admin.GetFromJsonAsync<CategoryView>(url))!;
        var duplicate = await admin.PutAsJsonAsync(url, new CategoryUpdateRequest { Revision = category.Revision, Name = (await admin.GetFromJsonAsync<CategoryView>(Catalog + $"categories/{second.CategoryId}"))!.Name });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode); Assert.Contains("Name", (await duplicate.Content.ReadFromJsonAsync<ApiError>())!.Errors!.Keys);
        Assert.Equal(category.Name, (await admin.GetFromJsonAsync<CategoryView>(url))!.Name);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync(url, new CategoryUpdateRequest { Revision = category.Revision, Name = "" })).StatusCode);
        var saved = await admin.PutAsJsonAsync(url, new CategoryUpdateRequest { Revision = category.Revision, Name = "Nueva categoría", Description = "Clasificación actualizada" });
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        Assert.Equal(product.CategoryId, await f.Db(db => db.BaseProducts.Where(x => x.Id == product.Id).Select(x => x.CategoryId).SingleAsync()));
        var audit = await f.Db(db => db.Events.SingleAsync(x => x.Action == "catalog_category_update" && x.Outcome == "Exitoso"));
        Assert.Contains(category.Name, audit.BeforeJson); Assert.Contains("Nueva", audit.AfterJson);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PutAsJsonAsync(url, new CategoryUpdateRequest { Revision = category.Revision, Name = "Cambio obsoleto" })).StatusCode);
    }

    [Fact]
    public async Task GOL016_ActiveProductsBlockDeactivationAndInactiveCategoryCannotBeAssigned()
    {
        var product = await Product(); using var admin = f.Http(await f.Login("admin@ameli.test"));
        var url = Catalog + $"categories/{product.CategoryId}";
        var category = (await admin.GetFromJsonAsync<CategoryView>(url))!;
        var blocked = await admin.PutAsJsonAsync(url + "/availability", new AvailabilityRequest { Revision = category.Revision, Reason = "Retiro temporal" });
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode); Assert.Contains("1", (await blocked.Content.ReadFromJsonAsync<ApiError>())!.Message);
        var edit = (await admin.GetFromJsonAsync<ProductEditView>(Catalog + $"products/{product.Id}"))!;
        await admin.PutAsJsonAsync(Catalog + $"products/{product.Id}/availability", new AvailabilityRequest { Revision = edit.Revision, Reason = "Retiro temporal" });
        var deactivated = await admin.PutAsJsonAsync(url + "/availability", new AvailabilityRequest { Revision = category.Revision, Reason = "Catálogo reorganizado" });
        category = (await deactivated.Content.ReadFromJsonAsync<CategoryView>())!; Assert.False(category.IsActive);
        var create = new BaseProductRequest { Name = "Nuevo producto", CategoryId = product.CategoryId, SupplierId = product.SupplierId, Species = "Perro", IsActive = true, UnitPrice = 1000 };
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync(Catalog + "products", create)).StatusCode);
        var productNow = (await admin.GetFromJsonAsync<ProductEditView>(Catalog + $"products/{product.Id}"))!;
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync(Catalog + $"products/{product.Id}/availability", new AvailabilityRequest { Revision = productNow.Revision, IsActive = true })).StatusCode);
        var active = await admin.PutAsJsonAsync(url + "/availability", new AvailabilityRequest { Revision = category.Revision, IsActive = true });
        Assert.True((await active.Content.ReadFromJsonAsync<CategoryView>())!.IsActive);
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync(Catalog + "products", create)).StatusCode);
    }

    [Fact]
    public async Task GOL006_ConcurrentPreparationAssignsExactlyOneResponsibleAndRetryIsIdempotent()
    {
        var order = await Order(await Product()); await f.AddUser("otra-logistica@ameli.test", Roles.Logistics);
        using var one = f.Http(await f.Login("logistica@ameli.test")); using var two = f.Http(await f.Login("otra-logistica@ameli.test"));
        var responses = await Task.WhenAll(one.PostAsJsonAsync(Staff(order.Id) + "/prepare", new { }), two.PostAsJsonAsync(Staff(order.Id) + "/prepare", new { }));
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.OK); Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Conflict);
        var taken = (await responses.Single(x => x.IsSuccessStatusCode).Content.ReadFromJsonAsync<OrderView>())!;
        var error = (await responses.Single(x => !x.IsSuccessStatusCode).Content.ReadFromJsonAsync<ApiError>())!;
        Assert.Contains(taken.Fulfillment!.ResponsibleName, error.Message); Assert.NotNull(taken.Fulfillment.PreparationStartedAtUtc);
        var responsible = taken.Fulfillment.ResponsibleUserId == f.Logistics ? one : two;
        var replay = await Ok(await responsible.PostAsJsonAsync(Staff(order.Id) + "/prepare", new { }));
        Assert.Equal(taken.Fulfillment.PreparationStartedAtUtc, replay.Fulfillment!.PreparationStartedAtUtc);
        Assert.Equal(1, await f.Db(db => db.OrderHistory.CountAsync(x => x.OrderId == order.Id && x.Status == OrderStatuses.Preparing)));
    }

    [Fact]
    public async Task GOL006_ReservationMismatchPersistsOneAlertAndCanBeCorrectedByAdmin()
    {
        var product = await Product(); var order = await Order(product);
        await f.Db(async db => { var item = await db.OrderItems.SingleAsync(x => x.OrderId == order.Id); item.ReservedQuantity = 1;
            var entity = await db.BaseProducts.SingleAsync(x => x.Id == product.Id); entity.StockQuantity++; await db.SaveChangesAsync(); });
        using var staff = f.Http(await f.Login("logistica@ameli.test"));
        var failed = await staff.PostAsJsonAsync(Staff(order.Id) + "/prepare", new { });
        Assert.Equal(HttpStatusCode.Conflict, failed.StatusCode); Assert.Contains(product.Name, (await failed.Content.ReadFromJsonAsync<ApiError>())!.Errors!["Items"].Single());
        Assert.Equal(OrderStatuses.Confirmed, await f.Db(db => db.Orders.Where(x => x.Id == order.Id).Select(x => x.Status).SingleAsync()));
        Assert.Equal(1, await f.Db(db => db.InventoryAlerts.CountAsync())); Assert.Equal(1, await f.Db(db => db.Emails.CountAsync()));
        await staff.PostAsJsonAsync(Staff(order.Id) + "/prepare", new { }); Assert.Equal(1, await f.Db(db => db.Emails.CountAsync()));
        var correction = new ReservationCorrectionRequest { Reason = "Recuento físico confirmado", Items = All(order) };
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.PutAsJsonAsync(Staff(order.Id) + "/reservations", correction)).StatusCode);
        using var admin = f.Http(await f.Login("admin@ameli.test")); await Ok(await admin.PutAsJsonAsync(Staff(order.Id) + "/reservations", correction));
        Assert.Equal(18, await f.Db(db => db.BaseProducts.Where(x => x.Id == product.Id).Select(x => x.StockQuantity).SingleAsync()));
        Assert.Equal(OrderStatuses.Preparing, (await Ok(await staff.PostAsJsonAsync(Staff(order.Id) + "/prepare", new { }))).Status);
    }

    [Fact]
    public async Task GOL006_UnpaidOrderCannotBePrepared()
    {
        var order = await Order(await Product());
        await f.Db(db => db.Orders.Where(x => x.Id == order.Id).ExecuteUpdateAsync(x => x.SetProperty(o => o.PaymentStatus, "Pendiente de verificación")));
        using var staff = f.Http(await f.Login("logistica@ameli.test"));
        var result = await staff.PostAsJsonAsync(Staff(order.Id) + "/prepare", new { }); Assert.Equal(HttpStatusCode.Conflict, result.StatusCode);
        Assert.Equal("payment_required", (await result.Content.ReadFromJsonAsync<ApiError>())!.Code);
        Assert.Null(await f.Db(db => db.Orders.Where(x => x.Id == order.Id).Select(x => x.PreparationUserId).SingleAsync()));
    }

    [Fact]
    public async Task GOL007_DispatchRequiresPackingAndBothFieldsNotifiesOnceAndGuideIsUnique()
    {
        var product = await Product(); var order = await Order(product); using var staff = f.Http(await f.Login("logistica@ameli.test"));
        await Ok(await staff.PostAsJsonAsync(Staff(order.Id) + "/prepare", new { }));
        var missing = await staff.PostAsJsonAsync(Staff(order.Id) + "/dispatch", new DispatchRequest());
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode); var errors = (await missing.Content.ReadFromJsonAsync<ApiError>())!.Errors!;
        Assert.Contains("Carrier", errors.Keys); Assert.Contains("TrackingNumber", errors.Keys);
        var command = new DispatchRequest { Carrier = "Correos", TrackingNumber = " CR-123 " };
        Assert.Equal(HttpStatusCode.Conflict, (await staff.PostAsJsonAsync(Staff(order.Id) + "/dispatch", command)).StatusCode);
        await Ok(await staff.PutAsJsonAsync(Staff(order.Id) + "/packing", new PackingRequest { Items = All(order) }));
        var shipped = await Ok(await staff.PostAsJsonAsync(Staff(order.Id) + "/dispatch", command)); Assert.Equal(OrderStatuses.Shipped, shipped.Status);
        await Ok(await staff.PostAsJsonAsync(Staff(order.Id) + "/dispatch", command));
        Assert.Equal(1, await f.Db(db => db.Emails.CountAsync())); Assert.Equal(1, await f.Db(db => db.OrderHistory.CountAsync(x => x.Status == OrderStatuses.Shipped)));
        var mail = await f.Db(db => db.Emails.SingleAsync());
        var body = f.Factory.Services.GetRequiredService<IDataProtectionProvider>().CreateProtector("Ameli.EmailOutbox.v1").Unprotect(mail.ProtectedBody);
        Assert.Contains("CR-123", body); Assert.Equal("cliente@ameli.test", mail.Recipient);
        var second = await Order(product); await Ok(await staff.PostAsJsonAsync(Staff(second.Id) + "/prepare", new { }));
        await Ok(await staff.PutAsJsonAsync(Staff(second.Id) + "/packing", new PackingRequest { Items = All(second) }));
        var duplicate = await staff.PostAsJsonAsync(Staff(second.Id) + "/dispatch", new DispatchRequest { Carrier = "Otro", TrackingNumber = "cr-123" });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode); Assert.Equal("duplicate_tracking", (await duplicate.Content.ReadFromJsonAsync<ApiError>())!.Code);
    }

    [Theory]
    [InlineData("Pendiente de pago")]
    [InlineData(OrderStatuses.Confirmed)]
    [InlineData(OrderStatuses.Cancelled)]
    [InlineData(OrderStatuses.Delivered)]
    public async Task GOL007_IllegalSequenceIsBlockedAlsoThroughTheLegacyApi(string status)
    {
        var order = await Order(await Product()); await f.Db(db => db.Orders.Where(x => x.Id == order.Id).ExecuteUpdateAsync(x => x.SetProperty(o => o.Status, status)));
        using var staff = f.Http(await f.Login("logistica@ameli.test"));
        Assert.Equal(HttpStatusCode.Conflict, (await staff.PutAsJsonAsync(Staff(order.Id), new OrderUpdateRequest { Status = OrderStatuses.LegacyShipped, Carrier = "Correos", TrackingNumber = "CR-123" })).StatusCode);
        Assert.Equal(status, await f.Db(db => db.Orders.Where(x => x.Id == order.Id).Select(x => x.Status).SingleAsync()));
    }

    [Fact]
    public async Task GOL008_DeliveryRequiresProofAndEveryItemRecordsIncidenceAndDoesNotDuplicateClosureOrRating()
    {
        var order = await Order(await Product()); using var staff = f.Http(await f.Login("logistica@ameli.test"));
        using var buyer = f.Http(await f.Login());
        using var admin = f.Http(await f.Login("admin@ameli.test"));
        Assert.Equal(HttpStatusCode.Conflict, (await buyer.PostAsJsonAsync($"/api/v1/assistant/orders/{order.Id}/rating", new PurchaseRatingRequest { Stars = 5 })).StatusCode);
        await Shipped(staff, order); f.Clock.Advance(TimeSpan.FromMinutes(1));
        var missing = await staff.PostAsJsonAsync(Staff(order.Id) + "/deliver", new DeliveryRequest { Items = All(order) });
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode); Assert.Equal(3, (await missing.Content.ReadFromJsonAsync<ApiError>())!.Errors!.Count);
        var delivery = new DeliveryRequest { DeliveredAtUtc = f.Clock.GetUtcNow(), ReceiptReference = "Recibe cliente", CarrierConfirmation = "POD-123", Items = All(order, 1) };
        var partial = await staff.PostAsJsonAsync(Staff(order.Id) + "/deliver", delivery); Assert.Equal(HttpStatusCode.Conflict, partial.StatusCode);
        Assert.Equal("incomplete_delivery", (await partial.Content.ReadFromJsonAsync<ApiError>())!.Code);
        var incident = new OrderIncidentRequest { RequestId = Guid.NewGuid(), Detail = "Falta una unidad en el paquete", MissingItems = All(order, 1) };
        await Ok(await staff.PostAsJsonAsync(Staff(order.Id) + "/incidents", incident)); await Ok(await staff.PostAsJsonAsync(Staff(order.Id) + "/incidents", incident));
        Assert.Equal(1, await f.Db(db => db.OrderIncidents.CountAsync()));
        Assert.Equal(OrderStatuses.Shipped, await f.Db(db => db.Orders.Where(x => x.Id == order.Id).Select(x => x.Status).SingleAsync()));
        delivery.Items = All(order); var complete = await Ok(await staff.PostAsJsonAsync(Staff(order.Id) + "/deliver", delivery));
        Assert.True(complete.Fulfillment!.PointsEnabled); Assert.True(complete.Fulfillment.RatingEnabled); Assert.Equal("logistica", complete.Fulfillment.DeliveryUserName);
        f.Clock.Advance(TimeSpan.FromMinutes(1));
        var duplicate = await Ok(await admin.PostAsJsonAsync(Staff(order.Id) + "/deliver", new DeliveryRequest()));
        Assert.Equal(complete.Fulfillment, duplicate.Fulfillment); Assert.Equal(1, await f.Db(db => db.OrderHistory.CountAsync(x => x.OrderId == order.Id && x.Status == OrderStatuses.Delivered)));
        Assert.Equal(HttpStatusCode.OK, (await buyer.PostAsJsonAsync($"/api/v1/assistant/orders/{order.Id}/rating", new PurchaseRatingRequest { Stars = 5, Comment = "Todo bien" })).StatusCode);
        await buyer.PostAsJsonAsync($"/api/v1/assistant/orders/{order.Id}/rating", new PurchaseRatingRequest { Stars = 1 }); Assert.Equal(1, await f.Db(db => db.PurchaseRatings.CountAsync()));
        Assert.Equal(5, await f.Db(db => db.PurchaseRatings.Select(x => x.Stars).SingleAsync()));
    }

    [Fact]
    public async Task CatalogAndLogisticsApisDenyUnauthorizedRolesAndLogTheAttempt()
    {
        var product = await Product(); var order = await Order(product);
        using var buyer = f.Http(await f.Login()); using var staff = f.Http(await f.Login("logistica@ameli.test"));
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync(Catalog + $"categories/{product.CategoryId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.PutAsJsonAsync(Catalog + $"categories/{product.CategoryId}", new CategoryUpdateRequest { Name = "No permitido", Revision = Guid.NewGuid() })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.PutAsJsonAsync(Catalog + $"products/{product.Id}/availability", new AvailabilityRequest { Reason = "No permitido", Revision = product.Revision })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await buyer.PostAsJsonAsync(Staff(order.Id) + "/prepare", new { })).StatusCode);
        Assert.True(await f.Db(db => db.Events.AnyAsync(x => x.Action == "http_access_denied" && x.ActorRole == Roles.Logistics)));
        Assert.True(await f.Db(db => db.BaseProducts.Where(x => x.Id == product.Id).Select(x => x.IsActive).SingleAsync()));
    }
}
