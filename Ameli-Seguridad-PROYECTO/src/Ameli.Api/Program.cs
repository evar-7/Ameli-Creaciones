using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;
using Ameli.Api.Application;
using Ameli.Api.Controllers;
using Ameli.Api.Domain;
using Ameli.Api.Infrastructure;
using Ameli.Contracts;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: false);
DevelopmentSetup.ConfigureSigningKey(builder.Configuration, builder.Environment);
builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);
builder.Logging.AddFilter("Microsoft.EntityFrameworkCore.Database.Command", LogLevel.Warning);
builder.WebHost.ConfigureKestrel(k => k.Limits.MaxRequestBodySize = 16 * 1024);
var connection = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Configura ConnectionStrings:DefaultConnection.");
builder.Services.AddDbContext<SecurityDbContext>(o => o.UseSqlServer(connection));
builder.Services.AddOptions<SecurityOptions>().Bind(builder.Configuration.GetSection("Security"))
    .Validate(o => o.FailedAttempts > 0 && o.LockoutMinutes > 0 && o.IdleMinutes > 0 && o.ResetMinutes > 0
        && o.AccessTokenMinutes > 0 && o.AbsoluteSessionHours > 0 && o.RecoveryAccountLimit > 0 && o.RecoveryOriginLimit > 0,
        "Los límites de seguridad deben ser mayores que cero.")
    .Validate(o => Uri.TryCreate(o.WebBaseUrl, UriKind.Absolute, out var url) && url.Scheme == Uri.UriSchemeHttps,
        "Security:WebBaseUrl debe ser una URL HTTPS absoluta.").ValidateOnStart();
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection("Jwt"));
var jwt = builder.Configuration.GetSection("Jwt").Get<JwtOptions>() ?? new();
if (Encoding.UTF8.GetByteCount(jwt.SigningKey) < 32)
    throw new InvalidOperationException("Configura Jwt:SigningKey con al menos 32 bytes aleatorios mediante user-secrets o variables de entorno.");
builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
builder.Services.AddSingleton<IPasswordHasher<AppUser>, PasswordHasher<AppUser>>();
builder.Services.Configure<PasswordHasherOptions>(o => o.IterationCount = 210_000);
builder.Services.AddSingleton<DummyPassword>();
builder.Services.AddScoped<SqlSecurityTransaction>();
builder.Services.AddScoped<SecurityService>();
builder.Services.AddSingleton<AuditIntegrity>();
builder.Services.AddScoped<TokenIssuer>();
builder.Services.AddScoped<IEmailDelivery, SmtpEmailDelivery>();
builder.Services.AddHostedService<EmailOutboxWorker>();
var keys = builder.Configuration["DataProtection:Directory"] ?? Path.Combine(builder.Environment.ContentRootPath, "App_Data", "keys");
Directory.CreateDirectory(keys);
var dataProtection = builder.Services.AddDataProtection().SetApplicationName("Ameli.Security.Api")
    .PersistKeysToFileSystem(new DirectoryInfo(keys));
if (OperatingSystem.IsWindows()) dataProtection.ProtectKeysWithDpapi();
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{ o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto; o.ForwardLimit = 1; });
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o =>
{
    o.MapInboundClaims = false;
    o.IncludeErrorDetails = false;
    o.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true, ValidIssuer = jwt.Issuer,
        ValidateAudience = true, ValidAudience = jwt.Audience,
        ValidateIssuerSigningKey = true, IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
        ValidateLifetime = true, RequireExpirationTime = true, ClockSkew = TimeSpan.Zero,
        ValidAlgorithms = [SecurityAlgorithms.HmacSha256], NameClaimType = "sub", RoleClaimType = "role"
    };
    o.Events = new JwtBearerEvents
    {
        OnTokenValidated = async context =>
        {
            if (!Guid.TryParse(context.Principal?.FindFirstValue("sub"), out var id)
                || !Guid.TryParse(context.Principal?.FindFirstValue("sid"), out var sessionId))
            { context.Fail("invalid_session"); return; }
            var status = await context.HttpContext.RequestServices.GetRequiredService<SecurityService>()
                .ValidateSessionAsync(new Actor(id, sessionId), context.HttpContext.RequestAborted);
            if (status is null || status.User.Role != context.Principal?.FindFirstValue("role")) context.Fail("invalid_session");
        }
    };
});
builder.Services.AddAuthorization();
builder.Services.AddControllers(o=>o.Filters.Add<AuditValidationFilter>()).ConfigureApiBehaviorOptions(o=>o.InvalidModelStateResponseFactory=c=>
    new Microsoft.AspNetCore.Mvc.BadRequestObjectResult(new ApiError("validation","Hay campos obligatorios incompletos o valores inválidos.",
        c.ModelState.Where(x=>x.Value?.Errors.Count>0).ToDictionary(x=>x.Key,x=>x.Value!.Errors.Select(e=>string.IsNullOrEmpty(e.ErrorMessage)?"Valor inválido.":e.ErrorMessage).ToArray()))));
builder.Services.AddOpenApi();
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = 429;
    o.OnRejected = async (ctx, ct) => await ctx.HttpContext.Response.WriteAsJsonAsync(
        new ApiError("rate_limit", "Demasiadas solicitudes. Intenta más tarde."), ct);
    foreach (var (name, limit) in new[] { ("auth", 60), ("recovery", 12) })
        o.AddPolicy(name, context => RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions
            { PermitLimit = limit, Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true }));
});
var app = builder.Build();
if (args.Contains("--initialize"))
{ await DatabaseInitializer.InitializeAsync(app.Services, app.Configuration, app.Environment); return; }
if (app.Environment.IsDevelopment() && app.Configuration.GetValue<bool>("Bootstrap:InitializeOnStartup"))
{
    await DatabaseInitializer.InitializeAsync(app.Services, app.Configuration, app.Environment);
    app.Logger.LogInformation("Base de datos de desarrollo preparada. Ya puedes ingresar desde Ameli.Web.");
}
app.UseForwardedHeaders();
if (!app.Environment.IsEnvironment("Testing")) app.UseHttpsRedirection();
app.Use(async (context, next) =>
{
    context.Response.Headers.CacheControl = "no-store";
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    try { await next(); }
    catch (SecurityFault ex)
    { context.Response.StatusCode = ex.Status; await context.Response.WriteAsJsonAsync(ex.ToError()); }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
        app.Logger.LogError(ex, "Falló una solicitud de seguridad. Correlación {Id}.", context.TraceIdentifier);
        context.Response.StatusCode = 500;
        await context.Response.WriteAsJsonAsync(new ApiError("server_error", "No se pudo completar la operación. Intenta nuevamente."));
    }
    finally
    {
        if(context.Response.StatusCode is >=400 and <500)
        {
            var db=context.RequestServices.GetRequiredService<SecurityDbContext>();
            if(!db.AuditRecorded)
            {
                db.ChangeTracker.Clear();var origin=HttpOrigin.Create(context);
                var id=Guid.TryParse(context.User.FindFirstValue("sub"),out var uid)?(Guid?)uid:null;
                var user=id.HasValue?await db.Users.AsNoTracking().SingleOrDefaultAsync(u=>u.Id==id):null;
                var path=context.Request.Path.Value??"";
                var action=context.Response.StatusCode is 401 or 403?"http_access_denied":path.Contains("internal-accounts")?"internal_request_rejected":path.EndsWith("/role")?"role_change":path.EndsWith("/export")?"audit_export":"request_rejected";
                if(path.EndsWith("/login"))action="login";
                else if(path.Contains("password"))action=path.EndsWith("reset-password")?"password_reset":path.EndsWith("change-password")?"password_change":"password_recovery";
                db.Events.Add(new SecurityEvent { OccurredAtUtc=DateTimeOffset.UtcNow,Action=action,Outcome=action=="login"?"Fallido":"Rechazado",Origin=origin.Ip,CorrelationId=origin.CorrelationId,
                    ActorUserId=id,ActorName=user?.Name??"",ActorEmail=user?.Email??"",ActorRole=user?.RoleName??"",SessionId=Guid.TryParse(context.User.FindFirstValue("sid"),out var sid)?sid:null,
                    SubjectUserId=context.Items["AuditSubjectId"] as Guid? ?? id,Entity="route",Detail=$"HTTP {context.Response.StatusCode}; {path}" });
                await db.SaveChangesAsync(CancellationToken.None);
            }
        }
    }
});
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapGet("/health", async (SecurityDbContext db, CancellationToken ct) => await db.Database.CanConnectAsync(ct) ? Results.Ok(new { status = "ok" }) : Results.StatusCode(503));
if (app.Environment.IsDevelopment()) app.MapOpenApi();
app.Run();
public partial class Program;
