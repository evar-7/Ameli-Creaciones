using System.Security.Cryptography;
using System.Text;

namespace Ameli.Api.Infrastructure;

// Convenience for local F5 only. Production always requires an explicit signing key.
public static class DevelopmentSetup
{
    public static void ConfigureSigningKey(ConfigurationManager config, IHostEnvironment environment)
    {
        if (!environment.IsDevelopment() || !string.IsNullOrWhiteSpace(config["Jwt:SigningKey"])) return;
        var directory = Path.Combine(environment.ContentRootPath, "App_Data");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "development-jwt.key");
        var fileOptions = new FileStreamOptions
        { Mode = FileMode.OpenOrCreate, Access = FileAccess.ReadWrite, Share = FileShare.None };
        if (!OperatingSystem.IsWindows()) fileOptions.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        using var file = new FileStream(path, fileOptions);
        string key;
        if (file.Length == 0)
        {
            key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
            file.Write(Encoding.UTF8.GetBytes(key));
            file.Flush(flushToDisk: true);
        }
        else
        {
            using var reader = new StreamReader(file, Encoding.UTF8, leaveOpen: true);
            key = reader.ReadToEnd().Trim();
            if (Convert.FromBase64String(key).Length < 32)
                throw new InvalidOperationException("La clave local de desarrollo está dañada. Configura Jwt:SigningKey en los secretos del proyecto.");
        }
        config.AddInMemoryCollection(new Dictionary<string, string?> { ["Jwt:SigningKey"] = key });
    }
}
