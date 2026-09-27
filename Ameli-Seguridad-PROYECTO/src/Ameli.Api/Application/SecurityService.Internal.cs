using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Ameli.Api.Domain;
using Ameli.Api.Infrastructure;
using Ameli.Contracts;
using Microsoft.EntityFrameworkCore;
namespace Ameli.Api.Application;
public sealed partial class SecurityService
{
    private static InternalAccountView View(AppUser u)=>new(u.Id,u.Name,u.Email,u.Phone,u.RoleName,u.IsActive,u.CreatedAtUtc,u.UpdatedAtUtc,u.Revision,u.LockedUntilUtc);
    // Explicit allowlist: never serialize the complete user entity into the audit log.
    private static string Snapshot(AppUser u)=>JsonSerializer.Serialize(new {u.Name,u.Email,u.Phone,Role=u.RoleName,u.IsActive,u.Revision});
    private static void ValidateInput(object input)
    {
        List<ValidationResult> errors=[];
        if(Validator.TryValidateObject(input,new(input),errors,true))return;
        throw new SecurityFault("validation","Hay campos obligatorios incompletos o valores inválidos.") {Errors=errors.SelectMany(e=>e.MemberNames.DefaultIfEmpty("").Select(n=>(n,e.ErrorMessage))).GroupBy(e=>e.n).ToDictionary(g=>g.Key,g=>g.Select(e=>e.ErrorMessage??"Valor inválido.").ToArray())};
    }
    private async Task<AppUser> RequireAdminAsync(Actor actor,CancellationToken ct)
    {var (u,_)=await RequireActorAsync(actor,ct);if(u.RoleName!=Roles.Administrator)throw new SecurityFault("forbidden","Acceso denegado.",403);return u;}
    public async Task<PagedResult<InternalAccountView>> InternalAccountsAsync(Actor actor,InternalAccountQuery q,CancellationToken ct)
    {
        await RequireAdminAsync(actor,ct);ValidateInput(q);var rows=db.Users.AsNoTracking().Where(u=>u.IsInternal);var search=q.Search?.Trim();
        if(!string.IsNullOrEmpty(search))rows=rows.Where(u=>u.Name.Contains(search)||u.Email.Contains(search));
        if(q.IsActive.HasValue)rows=rows.Where(u=>u.IsActive==q.IsActive.Value);
        var count=await rows.CountAsync(ct);var items=await rows.OrderBy(u=>u.Name).ThenBy(u=>u.Id).Skip((q.Page-1)*q.PageSize).Take(q.PageSize).ToListAsync(ct);
        return new(items.Select(View).ToList(),count,q.Page,q.PageSize);
    }
    public async Task<InternalAccountView> InternalAccountAsync(Actor actor,Guid id,CancellationToken ct)
    {await RequireAdminAsync(actor,ct);return View(await db.Users.AsNoTracking().SingleOrDefaultAsync(u=>u.Id==id&&u.IsInternal,ct)??throw new SecurityFault("not_found","Cuenta interna no encontrada.",404));}
    private SecurityEvent InternalAudit(string action,AppUser admin,Actor actor,Guid? id,RequestOrigin origin,string outcome="Exitoso",string detail="")
    {var e=Audit(action,outcome,origin,admin,id,detail);e.Module="Gestión interna";e.SubjectUserId=id;e.EntityId=id?.ToString()??"";e.SessionId=actor.SessionId;return e;}
    private static void Clean(InternalAccountRequest r){r.Name=r.Name?.Trim()??"";r.Email=r.Email?.Trim()??"";r.Phone=r.Phone?.Trim()??"";}
    private async Task CheckAccountAsync(InternalAccountRequest r,Guid? except,CancellationToken ct)
    {
        Clean(r);ValidateInput(r);var normalized=NormalizeEmail(r.Email);
        var duplicate=await db.Users.AsNoTracking().SingleOrDefaultAsync(u=>u.NormalizedEmail==normalized&&u.Id!=except,ct);
        if(duplicate is not null)throw new SecurityFault("duplicate_email","Ese correo ya está registrado.",409){ExistingId=duplicate.IsInternal?duplicate.Id:null,Errors=new(){["Email"]=["Debe ser único, incluso entre cuentas inactivas."]}};
        if(!await IsRoleActiveAsync(r.Role,ct))throw new SecurityFault("invalid_role","El rol no está disponible."){Errors=new(){["Role"]=["Selecciona un rol interno activo."]}};
    }
    public async Task<InternalAccountView> CreateInternalAsync(Actor actor,CreateInternalAccountRequest r,RequestOrigin origin,CancellationToken ct)
    {
        await using var tx=await transactions.BeginAsync(["ameli:administrators",SqlSecurityTransaction.User(actor.UserId)],ct);var admin=await RequireAdminAsync(actor,ct);
        try
        {
            await CheckAccountAsync(r,null,ct);ValidateNewPassword(r.Password,r.ConfirmPassword);
            var u=new AppUser {Name=r.Name,Email=r.Email,NormalizedEmail=NormalizeEmail(r.Email),Phone=r.Phone,RoleName=r.Role,IsInternal=true,IsActive=r.IsActive!.Value,CreatedAtUtc=Now,UpdatedAtUtc=Now};
            u.PasswordHash=passwords.HashPassword(u,r.Password);db.Users.Add(u);
            if(u.IsActive)
                QueueEmail(u.Email,"Tu cuenta interna de Ameli","Tu cuenta interna fue creada y ya tiene una contraseña asignada por administración. Si no conoces la contraseña o necesitas cambiarla, utiliza Recuperar contraseña desde la pantalla de ingreso.");
            InternalAudit("internal_account_create",admin,actor,u.Id,origin).AfterJson=Snapshot(u);
            await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);return View(u);
        }
        catch(SecurityFault e){InternalAudit("internal_account_create",admin,actor,null,origin,"Rechazado",e.Code);await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);throw;}
    }
    private static void Confirm(bool confirmed,string? reason)
    {if(!confirmed||string.IsNullOrWhiteSpace(reason)||reason.Trim().Length is <5 or >500)throw new SecurityFault("confirmation_required","Confirma el cambio de estado e indica un motivo de 5 a 500 caracteres."){Errors=new(){["Reason"]=["Indica el motivo y confirma la operación."]}};}
    public Task<InternalAccountView> UpdateInternalAsync(Actor actor,Guid id,UpdateInternalAccountRequest r,RequestOrigin origin,CancellationToken ct)=>EditAsync(actor,id,r.Revision,"internal_account_update",origin,async u=>
    {
        await CheckAccountAsync(r,id,ct);if(u.IsActive!=r.IsActive)Confirm(r.Confirmed,r.Reason);
        await ProtectLastAdministratorAsync(u,r.Role,r.IsActive!.Value,ct);
        var passwordRequested=!string.IsNullOrEmpty(r.Password)||!string.IsNullOrEmpty(r.ConfirmPassword);
        if(passwordRequested)ValidateNewPassword(r.Password??"",r.ConfirmPassword??"");
        if(u.RoleName!=r.Role||u.IsActive!=r.IsActive||u.Email!=r.Email||passwordRequested)await RevokeAllAsync(u,ct);
        if(u.Email!=r.Email||passwordRequested)foreach(var reset in await db.PasswordResets.Where(x=>x.UserId==id&&x.UsedAtUtc==null).ToListAsync(ct))reset.UsedAtUtc=Now;
        u.Name=r.Name;u.Email=r.Email;u.NormalizedEmail=NormalizeEmail(r.Email);u.Phone=r.Phone;u.RoleName=r.Role;u.IsActive=r.IsActive.Value;
        if(passwordRequested)u.PasswordHash=passwords.HashPassword(u,r.Password!);
        var reason=r.Reason?.Trim()??"";
        if(passwordRequested)reason=string.IsNullOrEmpty(reason)?"Contraseña actualizada por administración.":reason+" · Contraseña actualizada por administración.";
        return reason;
    },ct);
    public Task<InternalAccountView> SetInternalStateAsync(Actor actor,Guid id,InternalStateRequest r,RequestOrigin origin,CancellationToken ct)=>EditAsync(actor,id,r.Revision,"internal_account_status",origin,async u=>
    {
        ValidateInput(r);Confirm(r.Confirmed,r.Reason);await ProtectLastAdministratorAsync(u,u.RoleName,r.IsActive!.Value,ct);
        if(u.IsActive!=r.IsActive)await RevokeAllAsync(u,ct);u.IsActive=r.IsActive.Value;return r.Reason.Trim();
    },ct);
    private async Task<InternalAccountView> EditAsync(Actor actor,Guid id,Guid? revision,string action,RequestOrigin origin,Func<AppUser,Task<string>> change,CancellationToken ct)
    {
        await using var tx=await transactions.BeginAsync(["ameli:administrators",SqlSecurityTransaction.User(actor.UserId),SqlSecurityTransaction.User(id)],ct);
        var admin=await RequireAdminAsync(actor,ct);var role=admin.RoleName;var u=await db.Users.SingleOrDefaultAsync(u=>u.Id==id&&u.IsInternal,ct);
        try
        {
            if(u is null)throw new SecurityFault("not_found","Cuenta interna no encontrada.",404);
            if(u.Revision!=revision)throw new SecurityFault("concurrent_update","Otra persona modificó esta cuenta. Revisa la versión vigente antes de guardar.",409){Current=View(u)};
            var before=Snapshot(u);var reason=await change(u);
            db.ChangeTracker.DetectChanges();var changed=db.Entry(u).Properties.Any(p=>p.IsModified);
            if(changed){u.Revision=Guid.NewGuid();u.UpdatedAtUtc=Now;var e=InternalAudit(action,admin,actor,id,origin,detail:reason);e.ActorRole=role;e.BeforeJson=before;e.AfterJson=Snapshot(u);await db.SaveChangesAsync(ct);}
            await tx.CommitAsync(ct);return View(u);
        }
        catch(SecurityFault e){InternalAudit(action,admin,actor,id,origin,"Rechazado",e.Code);await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);throw;}
    }
}
