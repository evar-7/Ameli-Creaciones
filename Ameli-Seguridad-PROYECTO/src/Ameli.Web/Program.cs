using System.Security.Claims;
using Ameli.Contracts;
using Ameli.Web.Components;
using Ameli.Web.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: false);
builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);
builder.Logging.AddFilter("System.Net.Http.HttpClient", LogLevel.Warning);
builder.WebHost.ConfigureKestrel(k => k.Limits.MaxRequestBodySize = 16 * 1024);
builder.Services.AddRazorComponents();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddHttpContextAccessor();
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<ApiSessionStore>();
builder.Services.AddScoped<SecurityApiClient>();
builder.Services.AddHttpClient("Api", c =>
{ c.BaseAddress = new Uri(builder.Configuration["Api:BaseUrl"] ?? "https://localhost:7241/api/v1/"); c.Timeout = TimeSpan.FromSeconds(20); });
builder.Services.AddAntiforgery(o => { o.HeaderName = "X-CSRF-TOKEN"; o.Cookie.SecurePolicy = CookieSecurePolicy.Always; });
var keys = builder.Configuration["DataProtection:Directory"] ?? Path.Combine(builder.Environment.ContentRootPath, "App_Data", "keys");
Directory.CreateDirectory(keys);
var dataProtection = builder.Services.AddDataProtection().SetApplicationName("Ameli.Security.Web").PersistKeysToFileSystem(new DirectoryInfo(keys));
if (OperatingSystem.IsWindows()) dataProtection.ProtectKeysWithDpapi();
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(o =>
{
    o.Cookie.Name = "__Host-Ameli.Session"; o.Cookie.HttpOnly = true;
    o.Cookie.SecurePolicy = CookieSecurePolicy.Always; o.Cookie.SameSite = SameSiteMode.Lax;
    o.LoginPath = "/ingresar"; o.AccessDeniedPath = "/sin-acceso";
    o.ExpireTimeSpan = TimeSpan.FromHours(8); o.SlidingExpiration = false;
    o.Events = new CookieAuthenticationEvents
    {
        OnValidatePrincipal = async context =>
        {
            var key = context.Principal?.FindFirstValue(SecurityApiClient.SessionKeyClaim);
            var status = key is null ? null : await context.HttpContext.RequestServices.GetRequiredService<SecurityApiClient>()
                .ValidateAsync(key, context.HttpContext.RequestAborted);
            if (status is null) { context.RejectPrincipal(); await context.HttpContext.SignOutAsync(); }
            else context.HttpContext.Items["SessionStatus"] = status;
        },
        OnRedirectToLogin = context =>
        {
            if (context.Request.Path.StartsWithSegments("/account")) context.Response.StatusCode = 401;
            else context.Response.Redirect("/ingresar?estado=expirada");
            return Task.CompletedTask;
        },
        OnRedirectToAccessDenied = async context =>
        {
            await context.HttpContext.RequestServices.GetRequiredService<SecurityApiClient>().SendAsync<object>(HttpMethod.Post,"auth/access-denied",new WebAccessDeniedRequest(context.Request.Path.Value??"/"),context.HttpContext.RequestAborted);
            if (context.Request.Path.StartsWithSegments("/account")) context.Response.StatusCode = 403;
            else context.Response.Redirect("/sin-acceso");
        }
    };
});
builder.Services.AddAuthorization();
var app = builder.Build();
if (!app.Environment.IsDevelopment()) app.UseHsts();
app.UseHttpsRedirection();
app.Use(async (ctx, next) =>
{
    ctx.Response.Headers.CacheControl = "no-store";
    ctx.Response.Headers["Referrer-Policy"] = "no-referrer";
    ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
    ctx.Response.Headers["Content-Security-Policy"] = "default-src 'self'; style-src 'self'; script-src 'self'; img-src 'self' data:; font-src 'self'; connect-src 'self'; frame-ancestors 'none'; form-action 'self'; base-uri 'self'";
    try { await next(); }
    catch (ApiFault ex)
    {
        ctx.Response.StatusCode = ex.Status;
        await ctx.Response.WriteAsJsonAsync(ex.Error);
    }
    catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
    {
        ctx.Response.StatusCode = 503;
        await ctx.Response.WriteAsync("El servicio de acceso no está disponible en este momento. Intenta nuevamente.");
    }
});
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
// A permitted page navigation is activity. Passive /account/session polling is not.
app.Use(async (context, next) =>
{
    if (HttpMethods.IsGet(context.Request.Method) && !context.Request.Path.StartsWithSegments("/account")
        && context.User.Identity?.IsAuthenticated == true
        && context.GetEndpoint()?.Metadata.GetOrderedMetadata<IAuthorizeData>().Count > 0)
        context.Items["SessionStatus"] = await context.RequestServices.GetRequiredService<SecurityApiClient>()
            .SendAsync<SessionStatus>(HttpMethod.Post, "auth/activity", ct: context.RequestAborted);
    await next();
});
app.UseAntiforgery();
app.MapGet("/", (HttpContext context) => Results.Redirect(context.User.Identity?.IsAuthenticated == true
    ? Roles.Home(context.User.FindFirstValue(ClaimTypes.Role) ?? Roles.Client) : "/ingresar"));
app.MapAccountEndpoints();
app.MapManagementEndpoints();
app.MapRazorComponents<App>();
app.Run();
