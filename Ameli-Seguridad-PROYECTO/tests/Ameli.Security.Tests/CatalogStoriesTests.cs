using Ameli.Api.Application;
using Ameli.Api.Domain;
using Ameli.Contracts;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static Ameli.Security.Tests.SecurityFixture;

namespace Ameli.Security.Tests;

public sealed class CatalogStoriesTests(SecurityFixture f) : IClassFixture<SecurityFixture>, IAsyncLifetime
{
    public Task InitializeAsync() => f.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task GOL009_RegisterBaseProduct_CreatesProductWithAudit()
    {
        var actor = Actor(await f.Login("admin@ameli.test"));
        var categoryId = await AddCategory("Accesorios");
        var supplierId = await AddSupplier("Taller Lavanda", "lavanda@ameli.test");

        var product = await f.WithService(s => s.CreateBaseProductAsync(actor, new BaseProductRequest
        {
            Name = "Arnés confort lavanda",
            Description = "Ajustable y acolchado.",
            CategoryId = categoryId,
            SupplierId = supplierId,
            Species = ProductSpecies.Dog,
            Material = "Lona",
            Icon = "🦮",
            UnitPrice = 12900,
            StockQuantity = 18,
            IsActive = true
        }, Origin, default));

        Assert.Equal("Arnés confort lavanda", product.Name);
        Assert.Equal("Accesorios", product.CategoryName);
        Assert.Equal("Taller Lavanda", product.SupplierName);
        Assert.Equal(1, await f.Db(db => db.BaseProducts.CountAsync()));
        Assert.Equal(1, await f.Db(db => db.Events.CountAsync(e => e.Action == "catalog_product_create" && e.Outcome == "Exitoso")));
    }

    [Fact]
    public async Task GOL010_FilterBaseProducts_BySearchAndReferences()
    {
        var actor = Actor(await f.Login("admin@ameli.test"));
        var accessories = await AddCategory("Accesorios");
        var clothing = await AddCategory("Ropa");
        var lavanda = await AddSupplier("Taller Lavanda", "lavanda@ameli.test");
        var bosque = await AddSupplier("Bosque Textil", "bosque@ameli.test");
        await AddProduct("Arnés confort lavanda", accessories, lavanda, ProductSpecies.Dog, true);
        await AddProduct("Suéter tejido bosque", clothing, bosque, ProductSpecies.Dog, false);

        var filtered = await f.WithService(s => s.BaseProductsAsync(actor, new BaseProductQuery
        {
            Search = "lavanda",
            CategoryId = accessories,
            SupplierId = lavanda,
            Species = ProductSpecies.Dog,
            IsActive = true
        }, default));

        Assert.Single(filtered.Items);
        Assert.Equal("Arnés confort lavanda", filtered.Items[0].Name);
    }

    [Fact]
    public async Task GOL013_RegisterCategory_RejectsDuplicateName()
    {
        var actor = Actor(await f.Login("admin@ameli.test"));
        await AddCategory("Accesorios");

        var error = await Assert.ThrowsAsync<SecurityFault>(() => f.WithService(s => s.CreateCategoryAsync(actor, new CategoryRequest
        {
            Name = " accesorios ",
            Description = "Duplicada",
            IsActive = true
        }, Origin, default)));

        Assert.Equal("duplicate_category", error.Code);
    }

    [Fact]
    public async Task GOL014_FilterCategories_BySearchAndStatus()
    {
        var actor = Actor(await f.Login("admin@ameli.test"));
        await AddCategory("Accesorios", true, "Uso diario");
        await AddCategory("Descanso", false, "Camas y mantas");

        var filtered = await f.WithService(s => s.CategoriesAsync(actor, new CategoryQuery
        {
            Search = "desc",
            IsActive = false
        }, default));

        Assert.Single(filtered.Items);
        Assert.Equal("Descanso", filtered.Items[0].Name);
    }

    [Fact]
    public async Task GOL017_RegisterSupplier_RejectsDuplicateEmail()
    {
        var actor = Actor(await f.Login("admin@ameli.test"));
        await AddSupplier("Taller Lavanda", "lavanda@ameli.test");

        var error = await Assert.ThrowsAsync<SecurityFault>(() => f.WithService(s => s.CreateSupplierAsync(actor, new SupplierRequest
        {
            Name = "Otro nombre",
            ContactName = "Ana",
            Email = "lavanda@ameli.test",
            Phone = "88888888",
            Notes = "Duplicado",
            IsActive = true
        }, Origin, default)));

        Assert.Equal("duplicate_supplier", error.Code);
    }

    [Fact]
    public async Task GOL018_FilterSuppliers_BySearchAndStatus()
    {
        var actor = Actor(await f.Login("admin@ameli.test"));
        await AddSupplier("Taller Lavanda", "lavanda@ameli.test", true, "Ana");
        await AddSupplier("Bosque Textil", "bosque@ameli.test", false, "Luis");

        var filtered = await f.WithService(s => s.SuppliersAsync(actor, new SupplierQuery
        {
            Search = "bosque",
            IsActive = false
        }, default));

        Assert.Single(filtered.Items);
        Assert.Equal("Bosque Textil", filtered.Items[0].Name);
    }

    private Task<Guid> AddCategory(string name, bool active = true, string description = "") => f.Db(async db =>
    {
        var category = new ProductCategory { Name = name, NormalizedName = SecurityService.NormalizeEmail(name), Description = description, IsActive = active, CreatedAtUtc = f.Clock.GetUtcNow(), UpdatedAtUtc = f.Clock.GetUtcNow() };
        db.Categories.Add(category);
        await db.SaveChangesAsync();
        return category.Id;
    });

    private Task<Guid> AddSupplier(string name, string email, bool active = true, string contactName = "") => f.Db(async db =>
    {
        var supplier = new Supplier { Name = name, NormalizedName = SecurityService.NormalizeEmail(name), ContactName = contactName, Email = email, NormalizedEmail = SecurityService.NormalizeEmail(email), Phone = "88888888", Notes = "", IsActive = active, CreatedAtUtc = f.Clock.GetUtcNow(), UpdatedAtUtc = f.Clock.GetUtcNow() };
        db.Suppliers.Add(supplier);
        await db.SaveChangesAsync();
        return supplier.Id;
    });

    private Task<Guid> AddProduct(string name, Guid categoryId, Guid supplierId, string species, bool active) => f.Db(async db =>
    {
        var product = new BaseProduct { Name = name, NormalizedName = SecurityService.NormalizeEmail(name), Description = name, CategoryId = categoryId, SupplierId = supplierId, Species = species, Material = "Lona", Icon = "🎀", UnitPrice = 1000, StockQuantity = 5, IsActive = active, CreatedAtUtc = f.Clock.GetUtcNow(), UpdatedAtUtc = f.Clock.GetUtcNow() };
        db.BaseProducts.Add(product);
        await db.SaveChangesAsync();
        return product.Id;
    });
}
