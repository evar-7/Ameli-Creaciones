using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Ameli.Contracts;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace Ameli.Web.Security;

public static class AccountEndpoints
{
    public static void MapAccountEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/account");
        group.AddEndpointFilter(async (context, next) =>
        {
            if (!HttpMethods.IsGet(context.HttpContext.Request.Method))
            {
                try { await context.HttpContext.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(context.HttpContext); }
                catch (AntiforgeryValidationException) { return Results.BadRequest(new ApiError("invalid_form", "El formulario expiró. Recarga la página.")); }
            }
            return await next(context);
        });
        group.MapPost("/login", async (HttpContext ctx, SecurityApiClient api, ApiSessionStore store) =>
        {
            var form = await ctx.Request.ReadFormAsync();
            var request = new LoginRequest { Email = form["email"].ToString(), Password = form["password"].ToString() };
            if (!Valid(request)) return Results.Redirect("/ingresar?error=campos");
            try
            {
                var tokens = await api.AnonymousAsync<TokenResponse>("auth/login", request, ctx.RequestAborted);
                var key = store.Add(tokens);
                var user = tokens.Status.User;
                var identity = new ClaimsIdentity(new[]
                {
                    new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()), new Claim(ClaimTypes.Name, user.Name),
                    new Claim(ClaimTypes.Email, user.Email), new Claim(ClaimTypes.Role, user.Role),
                    new Claim(SecurityApiClient.SessionKeyClaim, key)
                }, CookieAuthenticationDefaults.AuthenticationScheme);
                await ctx.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity),
                    new AuthenticationProperties { IsPersistent = false, AllowRefresh = false, ExpiresUtc = tokens.Status.Session.AbsoluteExpiresAtUtc });
                return Results.Redirect(Roles.Home(user.Role));
            }
            catch (ApiFault ex) { return Results.Redirect(ex.Status == 429 ? "/ingresar?error=limite" : "/ingresar?error=credenciales"); }
        }).AllowAnonymous();
        group.MapPost("/forgot-password", async (HttpContext ctx, SecurityApiClient api) =>
        {
            var form = await ctx.Request.ReadFormAsync();
            var request = new ForgotPasswordRequest { Email = form["email"].ToString() };
            if (!Valid(request)) return Results.Redirect("/recuperar?error=campos");
            try { await api.AnonymousAsync<ApiMessage>("auth/forgot-password", request, ctx.RequestAborted); }
            catch (ApiFault ex) when (ex.Status == 429) { /* Same public answer at the origin limit. */ }
            return Results.Redirect("/recuperar?estado=enviado");
        }).AllowAnonymous();
        group.MapPost("/reset-password", async (HttpContext ctx, SecurityApiClient api) =>
        {
            var form = await ctx.Request.ReadFormAsync();
            var request = new ResetPasswordRequest { UserId = Guid.TryParse(form["userId"], out var id) ? id : Guid.Empty,
                Token = form["token"].ToString(), Password = form["password"].ToString(), ConfirmPassword = form["confirmPassword"].ToString() };
            var back = $"/restablecer?userId={request.UserId}&token={Uri.EscapeDataString(request.Token)}";
            if (!Valid(request)) return Results.Redirect(back + "&error=politica");
            try { await api.AnonymousAsync<ApiMessage>("auth/reset-password", request, ctx.RequestAborted); return Results.Redirect("/ingresar?estado=restablecida"); }
            catch (ApiFault ex) { return Results.Redirect(back + (ex.Code == "password_policy" ? "&error=politica" : "&error=enlace")); }
        }).AllowAnonymous();
        group.MapGet("/session", async (HttpContext ctx, SecurityApiClient api) =>
            Results.Ok(await api.SendAsync<SessionStatus>(HttpMethod.Get, "auth/session", ct: ctx.RequestAborted))).RequireAuthorization();
        group.MapPost("/activity", async (HttpContext ctx, SecurityApiClient api) =>
            Results.Ok(await api.SendAsync<SessionStatus>(HttpMethod.Post, "auth/activity", ct: ctx.RequestAborted))).RequireAuthorization();
        group.MapPost("/logout", async (HttpContext ctx, SecurityApiClient api) =>
        {
            try { await api.SendAsync<object>(HttpMethod.Post, "auth/logout", ct: ctx.RequestAborted); }
            catch (ApiFault ex) when (ex.Status == 401) { }
            api.ForgetCurrent(); await ctx.SignOutAsync(); return Results.Redirect("/ingresar?estado=cerrada");
        }).RequireAuthorization();
        group.MapPost("/logout-all", async (HttpContext ctx, SecurityApiClient api) =>
        {
            await api.SendAsync<object>(HttpMethod.Post, "auth/logout-all", ct: ctx.RequestAborted);
            api.ForgetCurrent(); await ctx.SignOutAsync(); return Results.Redirect("/ingresar?estado=cerrada");
        }).RequireAuthorization();
        group.MapPost("/revoke-session", async (HttpContext ctx, SecurityApiClient api) =>
        {
            var form = await ctx.Request.ReadFormAsync();
            if (!Guid.TryParse(form["sessionId"], out var id)) return Results.BadRequest();
            await api.SendAsync<object>(HttpMethod.Delete, $"auth/sessions/{id}", ct: ctx.RequestAborted);
            return Results.Redirect("/mi-seguridad?estado=sesion-cerrada");
        }).RequireAuthorization();
        group.MapPost("/change-password", async (HttpContext ctx, SecurityApiClient api) =>
        {
            var form = await ctx.Request.ReadFormAsync();
            var request = new ChangePasswordRequest { CurrentPassword = form["currentPassword"].ToString(),
                Password = form["password"].ToString(), ConfirmPassword = form["confirmPassword"].ToString() };
            if (!Valid(request)) return Results.Redirect("/mi-seguridad?error=politica");
            try
            {
                await api.SendAsync<ApiMessage>(HttpMethod.Post, "auth/change-password", request, ctx.RequestAborted);
                api.ForgetCurrent(); await ctx.SignOutAsync(); return Results.Redirect("/ingresar?estado=restablecida");
            }
            catch (ApiFault ex) { return Results.Redirect("/mi-seguridad?error=" + (ex.Code == "password_policy" ? "politica" : "clave")); }
        }).RequireAuthorization();
        foreach (var action in new[] { "role", "status", "unlock", "revoke-sessions" })
        {
            var currentAction = action;
            group.MapPost("/users/" + action, async (HttpContext ctx, SecurityApiClient api) =>
            {
                var form = await ctx.Request.ReadFormAsync();
                if (!Guid.TryParse(form["userId"], out var id)) return Results.BadRequest();
                object request = currentAction switch
                {
                    "role" => new ChangeRoleRequest { Role = form["role"].ToString() },
                    "status" => new ChangeStatusRequest { IsActive = form["isActive"] == "true", Reason = form["reason"].ToString() },
                    "revoke-sessions" => new RevokeUserSessionsRequest { Reason = form["reason"].ToString() },
                    _ => new UnlockRequest { Reason = form["reason"].ToString(), IdentityEvidence = form["identityEvidence"].ToString() }
                };
                if (!Valid(request)) return Results.Redirect("/administracion/accesos?error=campos");
                try
                {
                    await api.SendAsync<object>(currentAction is "unlock" or "revoke-sessions" ? HttpMethod.Post : HttpMethod.Put, $"admin/users/{id}/{currentAction}", request, ctx.RequestAborted);
                    return Results.Redirect("/administracion/accesos?estado=guardado");
                }
                catch (ApiFault ex)
                { return Results.Redirect("/administracion/accesos?error=" + Uri.EscapeDataString(ex.Code)); }
            }).RequireAuthorization(p => p.RequireRole(Roles.Administrator));
        }
    }
    private static bool Valid(object request) => Validator.TryValidateObject(request, new ValidationContext(request), [], true);
}
