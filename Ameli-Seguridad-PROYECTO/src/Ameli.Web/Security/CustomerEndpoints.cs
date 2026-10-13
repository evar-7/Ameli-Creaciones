using Ameli.Contracts;
using Microsoft.AspNetCore.Antiforgery;

namespace Ameli.Web.Security;

public static class CustomerEndpoints
{
    public static void MapCustomerEndpoints(this WebApplication app)
    {
        var registration = app.MapGroup("/account/registration");
        registration.AddEndpointFilter(ValidateForm);
        registration.MapPost("/create", async (CustomerRegistrationRequest request, SecurityApiClient api, HttpContext ctx) =>
        {
            try
            {
                var result = await api.AnonymousAsync<ApiMessage>("auth/register", request, ctx.RequestAborted);
                return Results.Json(result, statusCode: 201);
            }
            catch (ApiFault fault) { return Results.Json(fault.Error, statusCode: fault.Status); }
        }).AllowAnonymous();

        var group = app.MapGroup("/account/profile").RequireAuthorization(p => p.RequireRole(Roles.Client));
        group.AddEndpointFilter(ValidateForm);
        group.MapGet("/pets", (SecurityApiClient api, HttpContext ctx) => api.SendAsync<List<PetView>>(HttpMethod.Get, "customer/pets", ct: ctx.RequestAborted));
        group.MapGet("/pets/{id:guid}", (Guid id, SecurityApiClient api, HttpContext ctx) =>
            api.SendAsync<PetView>(HttpMethod.Get, $"customer/pets/{id}", ct: ctx.RequestAborted));
        group.MapPost("/pets", (PetRequest request, SecurityApiClient api, HttpContext ctx) =>
            api.SendAsync<PetView>(HttpMethod.Post, "customer/pets", request, ctx.RequestAborted));
        group.MapPut("/pets/{id:guid}", (Guid id, PetRequest request, SecurityApiClient api, HttpContext ctx) =>
            api.SendAsync<PetChangeResult>(HttpMethod.Put, $"customer/pets/{id}", request, ctx.RequestAborted));
        group.MapPost("/pets/{id:guid}/deactivate", (Guid id, SecurityApiClient api, HttpContext ctx) =>
            api.SendAsync<PetChangeResult>(HttpMethod.Post, $"customer/pets/{id}/deactivate", ct: ctx.RequestAborted));
        group.MapPost("/pets/{id:guid}/reactivate", (Guid id, SecurityApiClient api, HttpContext ctx) =>
            api.SendAsync<PetChangeResult>(HttpMethod.Post, $"customer/pets/{id}/reactivate", ct: ctx.RequestAborted));
        group.MapGet("/pets/{id:guid}/preferences", (Guid id, SecurityApiClient api, HttpContext ctx) =>
            api.SendAsync<PetPreferencesView>(HttpMethod.Get, $"customer/pets/{id}/preferences", ct: ctx.RequestAborted));
        group.MapPut("/pets/{id:guid}/preferences", (Guid id, PetPreferencesRequest request, SecurityApiClient api, HttpContext ctx) =>
            api.SendAsync<PetPreferencesView>(HttpMethod.Put, $"customer/pets/{id}/preferences", request, ctx.RequestAborted));
        group.MapGet("/pets/{id:guid}/photo", async (Guid id, SecurityApiClient api, HttpContext ctx) =>
        {
            var payload = await api.SendAsync<PetPhotoPayload>(HttpMethod.Get, $"customer/pets/{id}/photo", ct: ctx.RequestAborted);
            return Results.File(payload.Content, payload.ContentType);
        });
        group.MapPost("/pets/{id:guid}/photo", async (Guid id, HttpContext ctx, SecurityApiClient api) =>
        {
            var form = await ctx.Request.ReadFormAsync(ctx.RequestAborted);
            var file = form.Files.GetFile("photo");
            if (file is null) return Results.BadRequest(new ApiError("invalid_image", "Selecciona una imagen JPG, PNG o WEBP.",
                new() { ["Photo"] = ["Selecciona una imagen."] }));
            if (file.Length > 5 * 1024 * 1024) return Results.BadRequest(new ApiError("large_image", "No se admiten imagenes superiores a 5 MB",
                new() { ["Photo"] = ["No se admiten imagenes superiores a 5 MB"] }));
            await using var stream = file.OpenReadStream();
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, ctx.RequestAborted);
            return Results.Ok(await api.SendAsync<PetChangeResult>(HttpMethod.Post, $"customer/pets/{id}/photo",
                new PetPhotoRequest { FileName = file.FileName, Base64Data = Convert.ToBase64String(buffer.ToArray()) }, ctx.RequestAborted));
        });
        group.MapGet("/addresses", (SecurityApiClient api, HttpContext ctx) =>
            api.SendAsync<List<DeliveryAddressView>>(HttpMethod.Get, "customer/addresses", ct: ctx.RequestAborted));
        group.MapPost("/addresses", (DeliveryAddressRequest request, SecurityApiClient api, HttpContext ctx) =>
            api.SendAsync<DeliveryAddressView>(HttpMethod.Post, "customer/addresses", request, ctx.RequestAborted));
        group.MapPost("/addresses/{id:guid}/default", (Guid id, SecurityApiClient api, HttpContext ctx) =>
            api.SendAsync<DeliveryAddressView>(HttpMethod.Post, $"customer/addresses/{id}/default", ct: ctx.RequestAborted));
        group.MapDelete("/addresses/{id:guid}", async (Guid id, SecurityApiClient api, HttpContext ctx) =>
        {
            await api.SendAsync<object>(HttpMethod.Delete, $"customer/addresses/{id}", ct: ctx.RequestAborted);
            return Results.NoContent();
        });
    }

    private static async ValueTask<object?> ValidateForm(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        if (!HttpMethods.IsGet(http.Request.Method))
        {
            try { await http.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(http); }
            catch (AntiforgeryValidationException)
            { return Results.BadRequest(new ApiError("invalid_form", "El formulario expiró. Recarga la página.")); }
        }
        return await next(context);
    }
}
