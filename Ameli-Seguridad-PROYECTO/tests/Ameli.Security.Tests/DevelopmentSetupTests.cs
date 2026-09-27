using Ameli.Api.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Ameli.Security.Tests;

public sealed class LocalTestEnvironment(string directory, string name) : IWebHostEnvironment
{
    public string ApplicationName { get; set; } = "Ameli.Api";
    public string EnvironmentName { get; set; } = name;
    public string ContentRootPath { get; set; } = directory;
    public string WebRootPath { get; set; } = directory;
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
}

public sealed class DevelopmentSetupTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "ameli-setup-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void DevelopmentKeyIsRandomAndPersistsAcrossRestarts()
    {
        var environment = new LocalTestEnvironment(directory, Environments.Development);
        var first = new ConfigurationManager();
        DevelopmentSetup.ConfigureSigningKey(first, environment);
        var key = first["Jwt:SigningKey"]!;
        Assert.Equal(48, Convert.FromBase64String(key).Length);
        var restarted = new ConfigurationManager();
        DevelopmentSetup.ConfigureSigningKey(restarted, environment);
        Assert.Equal(key, restarted["Jwt:SigningKey"]);
        Assert.Equal(first["Audit:IntegrityKey"], restarted["Audit:IntegrityKey"]);
        Assert.NotEqual(key, first["Audit:IntegrityKey"]);
    }

    [Fact]
    public void ProductionDoesNotCreateOrLoadADevelopmentKey()
    {
        DevelopmentSetup.ConfigureSigningKey(new ConfigurationManager(), new LocalTestEnvironment(directory, Environments.Development));
        var production = new ConfigurationManager();
        DevelopmentSetup.ConfigureSigningKey(production, new LocalTestEnvironment(directory, Environments.Production));
        Assert.Null(production["Jwt:SigningKey"]);
    }

    [Fact]
    public void ExplicitSigningKeyIsPreserved()
    {
        var config = new ConfigurationManager { ["Jwt:SigningKey"] = "explicit-key-used-only-for-this-test", ["Audit:IntegrityKey"] = "explicit-audit-key-used-only-for-this-test" };
        DevelopmentSetup.ConfigureSigningKey(config, new LocalTestEnvironment(directory, Environments.Development));
        Assert.Equal("explicit-key-used-only-for-this-test", config["Jwt:SigningKey"]);
        Assert.False(Directory.Exists(directory));
    }

    public void Dispose()
    { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
}
