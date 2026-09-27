using Ameli.Contracts;
using Microsoft.AspNetCore.Antiforgery;
namespace Ameli.Web.Security;
public static class ManagementEndpoints
{
    public static void MapManagementEndpoints(this WebApplication app)
    {
        var g=app.MapGroup("/account/management").RequireAuthorization(p=>p.RequireRole(Roles.Administrator));
        g.AddEndpointFilter(async(c,next)=>
        {
            if(!HttpMethods.IsGet(c.HttpContext.Request.Method))try{await c.HttpContext.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(c.HttpContext);}
            catch(AntiforgeryValidationException){return Results.BadRequest(new ApiError("invalid_form","El formulario expiró. Recarga la página."));}
            return await next(c);
        });
        g.MapGet("/accounts",async(HttpContext c,SecurityApiClient api)=>Results.Ok(await api.SendAsync<PagedResult<InternalAccountView>>(HttpMethod.Get,"admin/internal-accounts"+c.Request.QueryString,ct:c.RequestAborted)));
        g.MapGet("/accounts/{id:guid}",async(Guid id,SecurityApiClient api,CancellationToken ct)=>Results.Ok(await api.SendAsync<InternalAccountView>(HttpMethod.Get,$"admin/internal-accounts/{id}",ct:ct)));
        g.MapPost("/accounts",async(CreateInternalAccountRequest r,SecurityApiClient api,CancellationToken ct)=>Results.Ok(await api.SendAsync<InternalAccountView>(HttpMethod.Post,"admin/internal-accounts",r,ct)));
        g.MapPost("/accounts/{id:guid}",async(Guid id,UpdateInternalAccountRequest r,SecurityApiClient api,CancellationToken ct)=>Results.Ok(await api.SendAsync<InternalAccountView>(HttpMethod.Put,$"admin/internal-accounts/{id}",r,ct)));
        g.MapPost("/accounts/{id:guid}/state",async(Guid id,InternalStateRequest r,SecurityApiClient api,CancellationToken ct)=>Results.Ok(await api.SendAsync<InternalAccountView>(HttpMethod.Put,$"admin/internal-accounts/{id}/state",r,ct)));
        g.MapGet("/audit",async(HttpContext c,SecurityApiClient api)=>Results.Ok(await api.SendAsync<PagedResult<AuditEventView>>(HttpMethod.Get,"admin/audit"+c.Request.QueryString,ct:c.RequestAborted)));
        g.MapPost("/audit/verify",async(AuditQuery r,SecurityApiClient api,CancellationToken ct)=>Results.Ok(await api.SendAsync<IntegrityReport>(HttpMethod.Post,"admin/audit/verify",r,ct)));
        g.MapPost("/audit/export",async(AuditExportRequest r,SecurityApiClient api,CancellationToken ct)=>Results.File(await api.SendAsync<byte[]>(HttpMethod.Post,"admin/audit/export",r,ct),"application/zip","Ameli-Auditoria.zip"));
    }
}
