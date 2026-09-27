using System.Net.Http.Headers;
using System.Security.Cryptography;
using Ameli.Api.Application;
using Ameli.Api.Domain;
using Ameli.Api.Infrastructure;
using Ameli.Contracts;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]
namespace Ameli.Security.Tests;

public sealed class TestClock : TimeProvider
{
    private DateTimeOffset now = DateTimeOffset.UtcNow;
    public override DateTimeOffset GetUtcNow() => now;
    public void Reset() => now = DateTimeOffset.UtcNow;
    public void Advance(TimeSpan amount) => now += amount;
}
public sealed class TestApiFactory(TestClock clock) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<TimeProvider>(); services.AddSingleton<TimeProvider>(clock);
            services.RemoveAll<IDataProtectionProvider>();
            services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
            foreach (var descriptor in services.Where(d => d.ImplementationType == typeof(EmailOutboxWorker)).ToArray())
                services.Remove(descriptor); // Tests inspect the encrypted outbox; never send mail.
        });
    }
}
public sealed class SecurityFixture : IAsyncLifetime
{
    public const string Password = "Prueba-Segura-2026!";
    public const string NewPassword = "Nueva-Clave-2026!";
    public static readonly RequestOrigin Origin = new("127.0.0.1", "Integration tests", "security-test");
    public TestClock Clock { get; } = new();
    public TestApiFactory Factory { get; private set; } = null!;
    public Guid Admin { get; private set; }
    public Guid Client { get; private set; }
    public Guid Logistics { get; private set; }
    public string SigningKey { get; } = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
    private readonly Dictionary<string, string?> previousEnvironment = [];
    public async Task InitializeAsync()
    {
        var configured = Environment.GetEnvironmentVariable("AMELI_TEST_SQL")
            ?? @"Server=(localdb)\MSSQLLocalDB;Integrated Security=true;Encrypt=true;TrustServerCertificate=true";
        var connection = new SqlConnectionStringBuilder(configured) { InitialCatalog = "AmeliSecurityTests_" + Guid.NewGuid().ToString("N") };
        SetEnvironment("ConnectionStrings__DefaultConnection", connection.ConnectionString);
        SetEnvironment("Jwt__SigningKey", SigningKey);
        SetEnvironment("Audit__IntegrityKey", Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)));
        Factory = new TestApiFactory(Clock);
        await Db(async db => { await db.Database.MigrateAsync(); });
    }
    private void SetEnvironment(string key, string value)
    { previousEnvironment[key] = Environment.GetEnvironmentVariable(key); Environment.SetEnvironmentVariable(key, value); }
    public async Task ResetAsync()
    {
        Clock.Reset();
        await Db(async db =>
        {
            await db.Emails.ExecuteDeleteAsync(); await db.Events.ExecuteDeleteAsync(); await db.AuditHeads.ExecuteDeleteAsync();
            await db.RecoveryAttempts.ExecuteDeleteAsync(); await db.PasswordResets.ExecuteDeleteAsync();
            await db.Sessions.ExecuteDeleteAsync(); await db.Users.ExecuteDeleteAsync();
            await db.Roles.ExecuteUpdateAsync(s => s.SetProperty(r => r.IsActive, true));
        });
        Admin = await AddUser("admin@ameli.test", Roles.Administrator);
        Client = await AddUser("cliente@ameli.test", Roles.Client);
        Logistics = await AddUser("logistica@ameli.test", Roles.Logistics);
    }
    public async Task<Guid> AddUser(string email, string role, bool active = true)
    {
        using var scope = Factory.Services.CreateScope();
        var user = new AppUser { Name = email.Split('@')[0], Email = email,
            NormalizedEmail = SecurityService.NormalizeEmail(email), RoleName = role,
            IsInternal = Roles.IsInternal(role), IsActive = active, CreatedAtUtc = Clock.GetUtcNow(), UpdatedAtUtc = Clock.GetUtcNow(), Phone = "88888888" };
        user.PasswordHash = scope.ServiceProvider.GetRequiredService<IPasswordHasher<AppUser>>().HashPassword(user, Password);
        var db = scope.ServiceProvider.GetRequiredService<SecurityDbContext>();
        db.Users.Add(user); await db.SaveChangesAsync(); return user.Id;
    }
    public async Task<T> WithService<T>(Func<SecurityService, Task<T>> action)
    { using var scope = Factory.Services.CreateScope(); return await action(scope.ServiceProvider.GetRequiredService<SecurityService>()); }
    public async Task WithService(Func<SecurityService, Task> action)
    { using var scope = Factory.Services.CreateScope(); await action(scope.ServiceProvider.GetRequiredService<SecurityService>()); }
    public async Task<T> Db<T>(Func<SecurityDbContext, Task<T>> action)
    { using var scope = Factory.Services.CreateScope(); return await action(scope.ServiceProvider.GetRequiredService<SecurityDbContext>()); }
    public async Task Db(Func<SecurityDbContext, Task> action)
    { using var scope = Factory.Services.CreateScope(); await action(scope.ServiceProvider.GetRequiredService<SecurityDbContext>()); }
    public Task<TokenResponse> Login(string email = "cliente@ameli.test", string password = Password) =>
        WithService(s => s.LoginAsync(new LoginRequest { Email = email, Password = password }, Origin, default));
    public static Actor Actor(TokenResponse response) => new(response.Status.User.Id, response.Status.Session.Id);
    public HttpClient Http(TokenResponse? token = null)
    {
        var client = Factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        if (token is not null) client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        return client;
    }
    public async Task<string> RequestReset()
    {
        await WithService(s => s.ForgotPasswordAsync(new() { Email = "cliente@ameli.test" }, Origin, default));
        var encrypted = await Db(db => db.Emails.Where(e => e.Subject == "Recupera tu acceso a Ameli").OrderByDescending(e => e.CreatedAtUtc).Select(e => e.ProtectedBody).FirstAsync());
        var body = Factory.Services.GetRequiredService<IDataProtectionProvider>().CreateProtector("Ameli.EmailOutbox.v1").Unprotect(encrypted);
        var match = System.Text.RegularExpressions.Regex.Match(body, @"token=([^\s]+)");
        Assert.True(match.Success); return Uri.UnescapeDataString(match.Groups[1].Value);
    }
    public Task ResetPassword(string token, string password = NewPassword) => WithService(s => s.ResetPasswordAsync(
        new() { UserId = Client, Token = token, Password = password, ConfirmPassword = password }, Origin, default));
    public async Task DisposeAsync()
    {
        if (Factory is not null)
        {
            await Db(async db => { await db.Database.EnsureDeletedAsync(); });
            await Factory.DisposeAsync();
        }
        foreach (var (key, value) in previousEnvironment) Environment.SetEnvironmentVariable(key, value);
    }
}
