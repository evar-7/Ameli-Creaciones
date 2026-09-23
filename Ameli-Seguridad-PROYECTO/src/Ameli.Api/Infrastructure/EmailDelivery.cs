using System.Net;
using System.Net.Mail;
using Ameli.Api.Domain;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace Ameli.Api.Infrastructure;

public interface IEmailDelivery
{
    Task SendAsync(string to, string subject, string body, CancellationToken ct);
}
public sealed class SmtpEmailDelivery(IConfiguration config, IWebHostEnvironment environment) : IEmailDelivery
{
    public async Task SendAsync(string to, string subject, string body, CancellationToken ct)
    {
        using var message = new MailMessage(config["Email:From"] ?? "acceso@ameli.example", to, subject, body);
        using var smtp = new SmtpClient();
        var mode = config["Email:Mode"] ?? "Smtp";
        if (mode == "Pickup")
        {
            if (!environment.IsDevelopment() && !environment.IsEnvironment("Testing"))
                throw new InvalidOperationException("Pickup solo se permite en desarrollo o pruebas.");
            var directory = Path.GetFullPath(config["Email:PickupDirectory"] ?? Path.Combine(environment.ContentRootPath, "App_Data", "mail"));
            Directory.CreateDirectory(directory);
            smtp.DeliveryMethod = SmtpDeliveryMethod.SpecifiedPickupDirectory;
            smtp.PickupDirectoryLocation = directory;
        }
        else
        {
            smtp.Host = config["Email:Host"] ?? throw new InvalidOperationException("Configura Email:Host.");
            smtp.Port = config.GetValue("Email:Port", 587);
            smtp.EnableSsl = true;
            smtp.Credentials = new NetworkCredential(config["Email:Username"], config["Email:Password"]);
            smtp.DeliveryMethod = SmtpDeliveryMethod.Network;
        }
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
