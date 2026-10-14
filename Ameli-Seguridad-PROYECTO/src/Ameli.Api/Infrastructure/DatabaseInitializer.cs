using System.Text.Json;
using Ameli.Api.Application;
using Ameli.Api.Domain;
using Ameli.Contracts;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Ameli.Api.Infrastructure;

public static class DatabaseInitializer
{
    public static async Task InitializeAsync(IServiceProvider services, IConfiguration config, IWebHostEnvironment environment)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SecurityDbContext>();
        await db.Database.MigrateAsync();
        await db.InitializeAuditAsync();
        await using var tx = await scope.ServiceProvider.GetRequiredService<SqlSecurityTransaction>()
            .BeginAsync(["ameli:bootstrap"], CancellationToken.None);
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<AppUser>>();
        var email = config["Bootstrap:AdminEmail"] ?? "";
        var password = config["Bootstrap:AdminPassword"] ?? "";
        if (string.IsNullOrWhiteSpace(email))
        {
            if (!await db.Users.AnyAsync(u => u.IsActive && u.RoleName == Roles.Administrator))
                throw new InvalidOperationException("En Visual Studio: clic derecho en Ameli.Api > Administrar secretos de usuario. Completa Bootstrap:AdminEmail y Bootstrap:AdminPassword según INICIAR-EN-VISUAL-STUDIO.md.");
        }
        else
        {
            if (!new System.ComponentModel.DataAnnotations.EmailAddressAttribute().IsValid(email))
                throw new InvalidOperationException("Bootstrap:AdminEmail debe ser un correo válido.");
            await AddUser(email, "Administración Ameli", Roles.Administrator, password);
        }
        if (environment.IsDevelopment() && config.GetValue<bool>("Bootstrap:CreateDemoUsers"))
        {
            var demoPassword = config["Bootstrap:DemoPassword"] ?? "";
            await AddUser("cliente@ameli.test", "Cliente de pruebas", Roles.Client, demoPassword);
            await AddUser("logistica@ameli.test", "Logística de pruebas", Roles.Logistics, demoPassword);
            await AddUser("admin2@ameli.test", "Segundo administrador", Roles.Administrator, demoPassword);
        }
        if (environment.IsDevelopment() && config.GetValue<bool>("Bootstrap:CreateDemoCatalog"))
            await SeedDemoCatalogAsync();
        await db.SaveChangesAsync();
        await tx.CommitAsync();

        async Task AddUser(string accountEmail, string name, string role, string accountPassword)
        {
            var normalized = SecurityService.NormalizeEmail(accountEmail);
            if (db.Users.Local.Any(x => x.NormalizedEmail == normalized)
                || await db.Users.AnyAsync(x => x.NormalizedEmail == normalized)) return;
            if (!SecurityService.IsStrongPassword(accountPassword))
                throw new InvalidOperationException("Completa las contraseñas de Bootstrap en Administrar secretos de usuario: 8-64 caracteres, mayúscula, minúscula, número y símbolo. No uses los marcadores de la plantilla.");
            var user = new AppUser
            {
                Name = name,
                Email = accountEmail.Trim(),
                NormalizedEmail = normalized,
                RoleName = role,
                IsInternal = Roles.IsInternal(role),
                CreatedAtUtc = DateTimeOffset.UtcNow,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            };
            user.PasswordHash = hasher.HashPassword(user, accountPassword);
            db.Users.Add(user);
        }

        async Task SeedDemoCatalogAsync()
        {
            var now = DateTimeOffset.UtcNow;

            var categoryClima = await EnsureCategoryAsync("Ropa de clima", "Prendas para temporada cálida o lluviosa.", now);
            var categoryPaseo = await EnsureCategoryAsync("Accesorios de paseo", "Opciones para caminatas y salidas.", now);
            var supplier = await EnsureSupplierAsync("Taller Demo Ameli", "Demo Proveedor", "proveedor.demo@ameli.test", "88887777", now);

            var chalecoDog = await EnsureProductAsync("Chaleco algodón azul", categoryClima, supplier, ProductSpecies.Dog,
                "Algodón transpirable", "🐕", 12990m, 24, "AML-DEMO-001", "Chaleco", "Casual", ["Azul", "Blanco"], now);
            var impermeableDog = await EnsureProductAsync("Impermeable liviano perro", categoryClima, supplier, ProductSpecies.Dog,
                "Impermeable", "🌧️", 16990m, 18, "AML-DEMO-002", "Abrigo", "Deportivo", ["Amarillo", "Negro"], now);
            var pecheraAll = await EnsureProductAsync("Pechera confort", categoryPaseo, supplier, ProductSpecies.All,
                "Malla transpirable", "🎽", 10990m, 30, "AML-DEMO-003", "Pechera", "Urbano", ["Rojo", "Gris"], now);
            var sueterCat = await EnsureProductAsync("Suéter suave gato", categoryClima, supplier, ProductSpecies.Cat,
                "Algodón", "🐈", 11990m, 20, "AML-DEMO-004", "Suéter", "Clásico", ["Verde", "Crema"], now);

            await EnsureSizesAsync(chalecoDog, [
                ("S", 18m, 30m, 1m, 4m, "Talla pequeña"),
                ("M", 30m, 42m, 4m, 9m, "Talla mediana"),
                ("L", 42m, 60m, 9m, 18m, "Talla grande")
            ]);
            await EnsureSizesAsync(impermeableDog, [
                ("S", 18m, 30m, 1m, 4m, "Ligero"),
                ("M", 30m, 42m, 4m, 9m, "Regular"),
                ("L", 42m, 60m, 9m, 18m, "Amplio")
            ]);
            await EnsureSizesAsync(pecheraAll, [
                ("S", 18m, 30m, 1m, 4m, "Ajuste firme"),
                ("M", 30m, 42m, 4m, 9m, "Ajuste medio"),
                ("L", 42m, 60m, 9m, 18m, "Ajuste amplio")
            ]);
            await EnsureSizesAsync(sueterCat, [
                ("XS", 15m, 25m, 1m, 3m, "Gato pequeño"),
                ("S", 25m, 35m, 3m, 6m, "Gato mediano"),
                ("M", 35m, 45m, 6m, 9m, "Gato grande")
            ]);

            var owner = await db.Users.FirstOrDefaultAsync(u => u.IsActive && u.RoleName == Roles.Client);
            if (owner is null) return;
            var petName = "Luna Demo";
            if (await db.Pets.AnyAsync(p => p.OwnerUserId == owner.Id && p.Name == petName)) return;
            db.Pets.Add(new PetProfile
            {
                OwnerUserId = owner.Id,
                Name = petName,
                Species = ProductSpecies.Dog,
                Breed = "Mestiza",
                AgeYears = 4,
                HeightCm = 36m,
                WeightKg = 8.5m,
                Allergies = "Lana",
                AllergiesDeclared = true,
                IsActive = true,
                ColorsJson = JsonSerializer.Serialize(new[] { "Azul", "Rojo" }),
                MaterialsJson = JsonSerializer.Serialize(new[] { "Algodón", "Malla" }),
                GarmentTypesJson = JsonSerializer.Serialize(new[] { "Chaleco", "Pechera" }),
                StylesJson = JsonSerializer.Serialize(new[] { "Casual", "Urbano" }),
                PreferencesUpdatedAtUtc = now,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
                Revision = Guid.NewGuid()
            });
        }

        async Task<Guid> EnsureCategoryAsync(string name, string description, DateTimeOffset now)
        {
            var normalized = ProductSizing.Fold(name);
            var existing = await db.Categories.SingleOrDefaultAsync(c => c.NormalizedName == normalized);
            if (existing is not null) return existing.Id;
            var category = new ProductCategory
            {
                Name = name,
                NormalizedName = normalized,
                Description = description,
                IsActive = true,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };
            db.Categories.Add(category);
            return category.Id;
        }

        async Task<Guid> EnsureSupplierAsync(string name, string contactName, string mail, string phone, DateTimeOffset now)
        {
            var normalizedMail = SecurityService.NormalizeEmail(mail);
            var existing = await db.Suppliers.SingleOrDefaultAsync(s => s.NormalizedEmail == normalizedMail);
            if (existing is not null) return existing.Id;
            var supplier = new Supplier
            {
                Name = name,
                NormalizedName = ProductSizing.Fold(name),
                ContactName = contactName,
                Email = mail,
                NormalizedEmail = normalizedMail,
                Phone = phone,
                Notes = "Generado automáticamente para pruebas manuales en Development.",
                IsActive = true,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };
            db.Suppliers.Add(supplier);
            return supplier.Id;
        }

        async Task<Guid> EnsureProductAsync(string name, Guid categoryId, Guid supplierId, string species, string material,
            string icon, decimal price, int stock, string sku, string garmentType, string style, string[] colors, DateTimeOffset now)
        {
            var normalized = ProductSizing.Fold(name);
            var existing = await db.BaseProducts.SingleOrDefaultAsync(p => p.NormalizedName == normalized);
            if (existing is not null) return existing.Id;
            var product = new BaseProduct
            {
                Name = name,
                NormalizedName = normalized,
                Description = "Producto de catálogo demo para recomendaciones.",
                CategoryId = categoryId,
                SupplierId = supplierId,
                Species = species,
                Material = material,
                Icon = icon,
                UnitPrice = price,
                StockQuantity = stock,
                IsActive = true,
                Sku = sku,
                CareInstructions = "Lavar a mano y secar a la sombra.",
                FitNotes = "Ajuste cómodo para uso diario.",
                ColorsJson = JsonSerializer.Serialize(colors),
                GarmentType = garmentType,
                Style = style,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };
            db.BaseProducts.Add(product);
            return product.Id;
        }

        async Task EnsureSizesAsync(Guid productId, (string Name, decimal MinHeightCm, decimal MaxHeightCm,
            decimal MinWeightKg, decimal MaxWeightKg, string Equivalent)[] sizes)
        {
            var existing = await db.ProductSizes.Where(s => s.ProductId == productId).Select(s => s.Name).ToListAsync();
            foreach (var size in sizes)
            {
                if (existing.Contains(size.Name, StringComparer.OrdinalIgnoreCase)) continue;
                db.ProductSizes.Add(new ProductSizeRule
                {
                    ProductId = productId,
                    Name = size.Name,
                    MinHeightCm = size.MinHeightCm,
                    MaxHeightCm = size.MaxHeightCm,
                    MinWeightKg = size.MinWeightKg,
                    MaxWeightKg = size.MaxWeightKg,
                    Equivalent = size.Equivalent
                });
            }
        }
    }
}
