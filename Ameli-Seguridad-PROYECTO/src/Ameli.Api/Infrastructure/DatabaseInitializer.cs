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
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        async Task AddUser(string accountEmail, string name, string role, string accountPassword)
        {
            var normalized = SecurityService.NormalizeEmail(accountEmail);
            if (db.Users.Local.Any(x => x.NormalizedEmail == normalized)
                || await db.Users.AnyAsync(x => x.NormalizedEmail == normalized)) return;
            if (!SecurityService.IsStrongPassword(accountPassword))
                throw new InvalidOperationException("Completa las contraseñas de Bootstrap en Administrar secretos de usuario: 8-64 caracteres, mayúscula, minúscula, número y símbolo. No uses los marcadores de la plantilla.");
            var user = new AppUser { Name = name, Email = accountEmail.Trim(), NormalizedEmail = normalized,
                RoleName = role, IsInternal = Roles.IsInternal(role), CreatedAtUtc = DateTimeOffset.UtcNow, UpdatedAtUtc = DateTimeOffset.UtcNow };
            user.PasswordHash = hasher.HashPassword(user, accountPassword);
            db.Users.Add(user);
        }
    }
}
