using Ameli.Api.Infrastructure;
using Ameli.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Ameli.Security.Tests;

public sealed class DatabaseInitializerTests(SecurityFixture f) : IClassFixture<SecurityFixture>, IAsyncLifetime
{
    private readonly LocalTestEnvironment environment = new(Path.GetTempPath(), Environments.Development);
    public async Task InitializeAsync()
    {
        await f.ResetAsync();
        await f.Db(async db => { await db.Users.ExecuteDeleteAsync(); });
    }
    public Task DisposeAsync() => Task.CompletedTask;
    private static ConfigurationManager Config(string demoPassword) => new()
    {
        ["Bootstrap:AdminEmail"] = "first@ameli.test",
        ["Bootstrap:AdminPassword"] = SecurityFixture.Password,
        ["Bootstrap:CreateDemoUsers"] = "true",
        ["Bootstrap:DemoPassword"] = demoPassword
    };

    [Fact]
    public async Task FirstStartCreatesAccountsAndRestartDoesNotNeedPasswordsOrReplaceAccounts()
    {
        var first = Config(SecurityFixture.Password);
        await Task.WhenAll(
            DatabaseInitializer.InitializeAsync(f.Factory.Services, first, environment),
            DatabaseInitializer.InitializeAsync(f.Factory.Services, first, environment));
        Assert.Equal(4, await f.Db(db => db.Users.CountAsync()));
        Assert.Equal(2, await f.Db(db => db.Users.CountAsync(u => u.RoleName == Roles.Administrator)));
        var before = await f.Db(db => db.Users.AsNoTracking().OrderBy(u => u.Email).ToListAsync());
        var restart = Config(""); restart["Bootstrap:AdminPassword"] = "";
        await DatabaseInitializer.InitializeAsync(f.Factory.Services, restart, environment);
        var after = await f.Db(db => db.Users.AsNoTracking().OrderBy(u => u.Email).ToListAsync());
        Assert.Equal(before.Select(u => (u.Id, u.RoleName, u.PasswordHash)), after.Select(u => (u.Id, u.RoleName, u.PasswordHash)));
        var login = await f.Login("first@ameli.test");
        Assert.Equal(Roles.Administrator, login.Status.User.Role);
    }

    [Fact]
    public async Task InvalidDemoPasswordDoesNotLeavePartialAccounts()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => DatabaseInitializer.InitializeAsync(f.Factory.Services, Config("weak"), environment));
        Assert.Equal(0, await f.Db(db => db.Users.CountAsync()));
    }
}
