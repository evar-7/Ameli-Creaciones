using System.Net;
using System.Net.Mail;
using System.Text;
using Ameli.Api.Domain;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace Ameli.Api.Infrastructure;

public interface IEmailDelivery
{
    Task SendAsync(string to, string subject, string body, CancellationToken ct);
}
public static class EmailConfiguration
{
    public static void Validate(IConfiguration config, bool isLocalEnvironment)
    {
        var mode = config["Email:Mode"] ?? "Smtp";
        if (mode.Equals("Pickup", StringComparison.OrdinalIgnoreCase))
        {
            if (!isLocalEnvironment)
                throw new InvalidOperationException("Email:Mode=Pickup solo se permite en desarrollo o pruebas.");
            return;
        }
        if (!mode.Equals("Smtp", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Email:Mode debe ser Smtp o Pickup.");
        var from = config["Email:From"];
        if (!MailAddress.TryCreate(from ?? "", out var address) || address is null ||
            address.Address.EndsWith(".example", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Configura Email:From con un remitente real autorizado por el proveedor SMTP.");
        if (string.IsNullOrWhiteSpace(config["Email:Host"]) ||
            string.IsNullOrWhiteSpace(config["Email:Username"]) ||
            string.IsNullOrWhiteSpace(config["Email:Password"]))
            throw new InvalidOperationException("Configura Email:Host, Email:Username y Email:Password en los secretos de Ameli.Api para enviar correos reales.");
        if (!int.TryParse(config["Email:Port"], out var port) || port is < 1 or > 65535)
            throw new InvalidOperationException("Email:Port debe ser un puerto SMTP válido compatible con STARTTLS.");
    }
}
public sealed class SmtpEmailDelivery(IConfiguration config, IWebHostEnvironment environment) : IEmailDelivery
{
    public async Task SendAsync(string to, string subject, string body, CancellationToken ct)
    {
        var from = new MailAddress(config["Email:From"] ?? "acceso@ameli.example");
        var recipient = new MailAddress(to);
        var mode = config["Email:Mode"] ?? "Smtp";
        if (mode.Equals("Pickup", StringComparison.OrdinalIgnoreCase))
        {
            if (!environment.IsDevelopment() && !environment.IsEnvironment("Testing"))
                throw new InvalidOperationException("Pickup solo se permite en desarrollo o pruebas.");
            var directory = Path.GetFullPath(config["Email:PickupDirectory"] ?? Path.Combine(environment.ContentRootPath, "App_Data", "mail"));
            Directory.CreateDirectory(directory);
            var file = Path.Combine(directory, $"{DateTime.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}.eml");
            var safeSubject = subject.Replace('\r', ' ').Replace('\n', ' ');
            var text = $"From: {from.Address}\r\nTo: {recipient.Address}\r\nDate: {DateTimeOffset.UtcNow:R}\r\nSubject: {safeSubject}\r\nMIME-Version: 1.0\r\nContent-Type: text/plain; charset=utf-8\r\nContent-Transfer-Encoding: 8bit\r\n\r\n{body}\r\n";
            await File.WriteAllTextAsync(file, text, new UTF8Encoding(false), ct);
            return;
        }

        using var message = new MailMessage(from, recipient) { Subject = subject, Body = body, BodyEncoding = Encoding.UTF8, SubjectEncoding = Encoding.UTF8 };
        using var smtp = new SmtpClient
        {
            Host = config["Email:Host"] ?? throw new InvalidOperationException("Configura Email:Host."),
            Port = config.GetValue("Email:Port", 587),
            EnableSsl = true,
            UseDefaultCredentials = false,
            Credentials = new NetworkCredential(config["Email:Username"], config["Email:Password"]),
            DeliveryMethod = SmtpDeliveryMethod.Network,
            Timeout = 20_000
        };
        await smtp.SendMailAsync(message, ct);
    }
}
public sealed class EmailOutboxWorker(IServiceScopeFactory scopes, ILogger<EmailOutboxWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try { await DeliverBatchAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError(ex, "No se pudo procesar la cola de correo."); }
        }
    }
    private async Task DeliverBatchAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SecurityDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<TimeProvider>();
        var protector = scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>().CreateProtector("Ameli.EmailOutbox.v1");
        var sender = scope.ServiceProvider.GetRequiredService<IEmailDelivery>();
        var now = clock.GetUtcNow();
        var ids = await db.Emails.AsNoTracking().Where(e => e.SentAtUtc == null && e.Attempts < 5 && e.NextAttemptAtUtc <= now)
            .OrderBy(e => e.CreatedAtUtc).Select(e => e.Id).Take(10).ToListAsync(ct);
        foreach (var id in ids)
        {
            await using var tx = await scope.ServiceProvider.GetRequiredService<SqlSecurityTransaction>()
                .BeginAsync([$"ameli:email:{id:N}"], ct);
            var email = await db.Emails.SingleAsync(e => e.Id == id, ct);
            if (email.SentAtUtc is not null || email.NextAttemptAtUtc > clock.GetUtcNow())
            { await tx.CommitAsync(ct); continue; }
            email.Attempts++;
            try
            {
                await sender.SendAsync(email.Recipient, email.Subject, protector.Unprotect(email.ProtectedBody), ct);
                email.SentAtUtc = clock.GetUtcNow();
                email.ProtectedBody = ""; // Do not retain reset links after delivery.
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                email.NextAttemptAtUtc = clock.GetUtcNow().AddMinutes(Math.Pow(2, email.Attempts));
                logger.LogWarning("Correo {EmailId} pendiente. Intento {Attempt}; tipo de error {ErrorType}.", email.Id, email.Attempts, ex.GetType().Name);
                db.Events.Add(new SecurityEvent { OccurredAtUtc = clock.GetUtcNow(), Action = "email_delivery", Outcome = "Fallido",
                    Detail = $"Mensaje {email.Id}; intento {email.Attempts}" });
            }
            await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        }
    }
}
