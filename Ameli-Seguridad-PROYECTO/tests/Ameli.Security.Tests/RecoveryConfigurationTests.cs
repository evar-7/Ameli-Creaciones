using Ameli.Api.Infrastructure;
using Ameli.Contracts;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Ameli.Security.Tests;

public sealed class RecoveryConfigurationTests
{
    [Fact]
    public void PasswordPolicyExplainsIndividualMissingRequirements()
    {
        Assert.Empty(PasswordPolicy.UnmetRequirements("ClaveSegura2026!"));
        Assert.Contains(PasswordPolicy.UnmetRequirements("abc"), x => x.Contains("8 y 64"));
        Assert.Contains(PasswordPolicy.UnmetRequirements("abcdefgh1!"), x => x.Contains("mayúscula"));
        Assert.Contains(PasswordPolicy.UnmetRequirements("ABCDEFGH1!"), x => x.Contains("minúscula"));
        Assert.Contains(PasswordPolicy.UnmetRequirements("Abcdefgh!"), x => x.Contains("número"));
        Assert.Contains(PasswordPolicy.UnmetRequirements("Abcdefgh1"), x => x.Contains("símbolo"));
    }

    [Fact]
    public void SmtpRequiresRealSenderAndCredentialsBeforeStarting()
    {
        var config = new ConfigurationManager
        {
            ["Email:Mode"] = "Smtp", ["Email:From"] = "acceso@ameli.example",
            ["Email:Port"] = "587"
        };
        Assert.Contains("Email:From", Assert.Throws<InvalidOperationException>(() => EmailConfiguration.Validate(config, true)).Message);
        config["Email:From"] = "acceso@ejemplo.test";
        Assert.Contains("Email:Host", Assert.Throws<InvalidOperationException>(() => EmailConfiguration.Validate(config, true)).Message);
        config["Email:Host"] = "smtp.ejemplo.test";
        config["Email:Username"] = "acceso@ejemplo.test";
        config["Email:Password"] = "credencial-de-prueba";
        EmailConfiguration.Validate(config, true);
        Assert.Throws<InvalidOperationException>(() => EmailConfiguration.Validate(new ConfigurationManager { ["Email:Mode"] = "Pickup" }, false));
    }
}
