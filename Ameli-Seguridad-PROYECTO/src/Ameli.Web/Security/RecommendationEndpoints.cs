using Ameli.Contracts;
using Microsoft.AspNetCore.Antiforgery;

namespace Ameli.Web.Security;

public static class RecommendationEndpoints
{
    public static void MapRecommendationEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/account/recommendations").RequireAuthorization(p => p.RequireRole(Roles.Client));
        group.AddEndpointFilter(async (context, next) =>
        {
            if (!HttpMethods.IsGet(context.HttpContext.Request.Method))
                try { await context.HttpContext.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(context.HttpContext); }
                catch (AntiforgeryValidationException) { return Results.BadRequest(new ApiError("invalid_form", "El formulario expiró. Recarga la página.")); }
            return await next(context);
        });
        group.MapGet("/pets", async (SecurityApiClient api, CancellationToken ct) =>
            Results.Ok((await api.SendAsync<List<PetView>>(HttpMethod.Get, "customer/pets", ct: ct)).Where(x => x.IsActive).ToList()));
        group.MapGet("/list", async (HttpContext context, SecurityApiClient api) =>
            Results.Ok(await api.SendAsync<RecommendationResultView>(HttpMethod.Get, "customer/recommendations" + context.Request.QueryString, ct: context.RequestAborted)));
        group.MapGet("/explanation", async (HttpContext context, SecurityApiClient api) =>
            Results.Ok(await api.SendAsync<RecommendationExplanationView>(HttpMethod.Get, "customer/recommendations/explanation" + context.Request.QueryString, ct: context.RequestAborted)));
        group.MapPost("/feedback", async (RecommendationFeedbackRequest request, SecurityApiClient api, CancellationToken ct) =>
            Results.Ok(await api.SendAsync<RecommendationFeedbackView>(HttpMethod.Post, "customer/recommendations/feedback", request, ct)));
    }
}
