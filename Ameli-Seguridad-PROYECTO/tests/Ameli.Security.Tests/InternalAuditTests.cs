using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Ameli.Api.Application;
using Ameli.Api.Infrastructure;
using Ameli.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;
using static Ameli.Security.Tests.SecurityFixture;
namespace Ameli.Security.Tests;
public sealed class InternalAuditTests(SecurityFixture f):IClassFixture<SecurityFixture>,IAsyncLifetime
{
    private Actor admin=null!;private TokenResponse token=null!;
    public async Task InitializeAsync(){await f.ResetAsync();token=await f.Login("admin@ameli.test");admin=Actor(token);}public Task DisposeAsync()=>Task.CompletedTask;
    private static CreateInternalAccountRequest New(string email="equipo@ameli.test")=>new(){Name="Equipo Ameli",Email=email,Phone="01234567",Role=Roles.Logistics,IsActive=true,Password=Password,ConfirmPassword=Password};
    private Task<InternalAccountView> Create(CreateInternalAccountRequest? r=null)=>f.WithService(s=>s.CreateInternalAsync(admin,r??New(),Origin,default));
    private Task<InternalAccountView> Get(Guid id)=>f.WithService(s=>s.InternalAccountAsync(admin,id,default));
    private static UpdateInternalAccountRequest Edit(InternalAccountView u)=>new(){Name=u.Name,Email=u.Email,Phone=u.Phone,Role=u.Role,IsActive=u.IsActive,Revision=u.Revision};
    private Task<InternalAccountView> Update(Guid id,UpdateInternalAccountRequest r)=>f.WithService(s=>s.UpdateInternalAsync(admin,id,r,Origin,default));
    private Task<InternalAccountView> State(InternalAccountView u,bool active,bool confirmed=true)=>f.WithService(s=>s.SetInternalStateAsync(admin,u.Id,new(){Revision=u.Revision,IsActive=active,Reason="Solicitud verificada",Confirmed=confirmed},Origin,default));
    private Task<IntegrityReport> Verify()=>f.WithService(s=>s.VerifyAuditAsync(admin,new(),Origin,default));
    [Fact]public async Task GUI001_CreateAndDuplicatesAreAuditedWithoutExposingClients()
    {
        var u=await Create();Assert.NotEqual(Guid.Empty,u.Id);Assert.Equal("01234567",u.Phone);Assert.Equal(0,await f.Db(db=>db.PasswordResets.CountAsync(x=>x.UserId==u.Id)));await f.Login(u.Email,Password);
        await State(u,false);var duplicate=await Assert.ThrowsAsync<SecurityFault>(()=>Create());Assert.Equal(u.Id,duplicate.ExistingId);
        var client=await Assert.ThrowsAsync<SecurityFault>(()=>Create(New("cliente@ameli.test")));Assert.Null(client.ExistingId);
        var e=await f.Db(db=>db.Events.SingleAsync(e=>e.Action=="internal_account_create"&&e.Outcome=="Exitoso"));Assert.Equal(f.Admin,e.ActorUserId);Assert.Equal(u.Id,e.SubjectUserId);Assert.DoesNotContain("PasswordHash",e.AfterJson);Assert.True((await Verify()).IsValid);
    }
    [Fact]public async Task GUI001_InvalidFieldsReturnErrorsAndDoNotCreate()
    {
        using var http=f.Http(token);var r=New();r.Name="";r.Phone="bad";r.Role=Roles.Client;
        var response=await http.PostAsJsonAsync("/api/v1/admin/internal-accounts",r);Assert.Equal(HttpStatusCode.BadRequest,response.StatusCode);Assert.NotEmpty((await response.Content.ReadFromJsonAsync<ApiError>())!.Errors!);Assert.Equal(3,await f.Db(db=>db.Users.CountAsync()));
    }
    [Fact]public async Task GUI002_FilterPaginationEmptyAndInternalPrivacy()
    {
        var r=New();r.IsActive=false;await Create(r);
        var all=await f.WithService(s=>s.InternalAccountsAsync(admin,new(),default));Assert.Equal(3,all.Total);Assert.DoesNotContain(all.Items,u=>u.Id==f.Client);
        var filtered=await f.WithService(s=>s.InternalAccountsAsync(admin,new(){Search="equipo@",IsActive=false},default));Assert.Single(filtered.Items);
        Assert.Empty((await f.WithService(s=>s.InternalAccountsAsync(admin,new(){Search="No existe"},default))).Items);
        Assert.Single((await f.WithService(s=>s.InternalAccountsAsync(admin,new(){PageSize=1,Page=2},default))).Items);await Assert.ThrowsAsync<SecurityFault>(()=>Get(f.Client));
    }
    [Theory][InlineData("cliente@ameli.test")][InlineData("logistica@ameli.test")]
    public async Task GUI002_SYA004_DeniedRolesCannotReadOrExport(string email)
    {
        using var http=f.Http(await f.Login(email));foreach(var path in new[]{"/api/v1/admin/internal-accounts","/api/v1/admin/audit"})Assert.Equal(HttpStatusCode.Forbidden,(await http.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,(await http.PostAsJsonAsync("/api/v1/admin/audit/export",new AuditExportRequest{Confirmed=true})).StatusCode);Assert.Equal(3,await f.Db(db=>db.Events.CountAsync(e=>e.Action=="http_access_denied")));
    }
    [Fact]public async Task GUI003_EditRejectsBadDataAndStaleVersionsPreservingIdentity()
    {
        var u=await Get(f.Logistics);var bad=Edit(u);bad.Phone="bad";await Assert.ThrowsAsync<SecurityFault>(()=>Update(u.Id,bad));Assert.Equal(u,await Get(u.Id));
        var good=Edit(u);good.Name="Nombre actualizado";var changed=await Update(u.Id,good);Assert.Equal(u.Id,changed.Id);Assert.NotEqual(u.Revision,changed.Revision);
        var fault=await Assert.ThrowsAsync<SecurityFault>(()=>Update(u.Id,good));Assert.Equal("concurrent_update",fault.Code);Assert.Equal(changed,fault.Current);
        var e=await f.Db(db=>db.Events.SingleAsync(e=>e.Action=="internal_account_update"&&e.Outcome=="Exitoso"));Assert.Contains("logistica",e.BeforeJson);Assert.Contains("Nombre actualizado",e.AfterJson);
    }
    [Fact]public async Task GUI003_AdminCanChangePasswordWhileEditingAccount()
    {
        var u=await Get(f.Logistics);var beforeHash=await f.Db(db=>db.Users.Where(x=>x.Id==u.Id).Select(x=>x.PasswordHash).SingleAsync());
        var session=await f.Login("logistica@ameli.test");var edit=Edit(u);edit.Password=NewPassword;edit.ConfirmPassword=NewPassword;
        var changed=await Update(u.Id,edit);Assert.NotEqual(u.Revision,changed.Revision);
        var afterHash=await f.Db(db=>db.Users.Where(x=>x.Id==u.Id).Select(x=>x.PasswordHash).SingleAsync());Assert.NotEqual(beforeHash,afterHash);Assert.Null(await f.WithService(s=>s.ValidateSessionAsync(Actor(session),default)));
        await Assert.ThrowsAsync<SecurityFault>(()=>f.Login("logistica@ameli.test",Password));await f.Login("logistica@ameli.test",NewPassword);
        var audit=await f.Db(db=>db.Events.SingleAsync(e=>e.Action=="internal_account_update"&&e.Outcome=="Exitoso"));Assert.Contains("Contraseña actualizada por administración",audit.Detail);Assert.DoesNotContain(NewPassword,JsonSerializer.Serialize(audit));
    }
    [Fact]public async Task GUI003_BlankPasswordOnEditKeepsExistingPassword()
    {
        var u=await Get(f.Logistics);var edit=Edit(u);edit.Name="Cambio sin clave";edit.Password=null;edit.ConfirmPassword=null;await Update(u.Id,edit);await f.Login("logistica@ameli.test",Password);
    }
    [Fact]public async Task GUI003_ConcurrentWritesHaveOneWinner()
    {
        var u=await Get(f.Logistics);var results=await Task.WhenAll(new[]{"Primera edición","Segunda edición"}.Select(async name=>{var r=Edit(u);r.Name=name;try{await Update(u.Id,r);return true;}catch(SecurityFault e){Assert.Equal("concurrent_update",e.Code);return false;}}));Assert.Single(results,x=>x);
    }
    [Fact]public async Task GUI004_StateChangesRequireConfirmationPreserveHistoryAndRevokeSessions()
    {
        var staff=await f.Login("logistica@ameli.test");var u=await Get(f.Logistics);await Assert.ThrowsAsync<SecurityFault>(()=>State(u,false,false));Assert.Equal(u,await Get(u.Id));
        var off=await State(u,false);Assert.Null(await f.WithService(s=>s.ValidateSessionAsync(Actor(staff),default)));await Assert.ThrowsAsync<SecurityFault>(()=>f.Login("logistica@ameli.test"));
        var on=await State(off,true);Assert.Equal(u.Id,on.Id);await f.Login("logistica@ameli.test");Assert.Equal(2,await f.Db(db=>db.Events.CountAsync(e=>e.Action=="internal_account_status"&&e.Outcome=="Exitoso")));
        var last=await Get(f.Admin);Assert.Equal("last_administrator",(await Assert.ThrowsAsync<SecurityFault>(()=>State(last,false))).Code);
    }
    [Fact]public async Task BIT001_LoginLockoutAndPasswordFailuresHaveMetadataWithoutSecrets()
    {
        var t=await f.Login();for(var i=0;i<5;i++)await Assert.ThrowsAsync<SecurityFault>(()=>f.Login(password:"wrong-private-value"));
        var login=await f.Db(db=>db.Events.SingleAsync(e=>e.Action=="login"&&e.SubjectUserId==f.Client&&e.Outcome=="Exitoso"));Assert.Equal(t.Status.Session.Id,login.SessionId);
        var locked=await f.Db(db=>db.Events.SingleAsync(e=>e.Action=="account_locked"));Assert.Equal(5,locked.FailedAttempts);Assert.Equal(TimeSpan.FromMinutes(15),locked.LockedUntilUtc-locked.LockoutStartedAtUtc);
        var reset=await f.RequestReset();await Assert.ThrowsAsync<SecurityFault>(()=>f.ResetPassword(reset,"weak"));await f.ResetPassword(reset);
        var events=await f.Db(db=>db.Events.ToListAsync());var json=JsonSerializer.Serialize(events);Assert.DoesNotContain(reset,json);Assert.DoesNotContain("wrong-private-value",json);Assert.DoesNotContain(NewPassword,json);
        Assert.Contains(events,e=>e.Action=="password_reset"&&e.Outcome=="Exitoso");Assert.Contains(events,e=>e.Action=="password_reset"&&e.Outcome=="Rechazado");
    }
    [Fact]public async Task SYA004_FiltersAndConfirmedExportHaveIntegrityManifest()
    {
        await Create();var day=DateOnly.FromDateTime(f.Clock.GetUtcNow().UtcDateTime);
        var list=await f.WithService(s=>s.AuditEventsAsync(admin,new(){From=day,To=day,User="admin@",Role=Roles.Administrator,Module="Gestión interna",Action="internal_account_create",Outcome="Exitoso",Ip=Origin.Ip},default));Assert.Single(list.Items);
        await Assert.ThrowsAsync<SecurityFault>(()=>f.WithService(s=>s.ExportAuditAsync(admin,new(),Origin,default)));
        var bytes=await f.WithService(s=>s.ExportAuditAsync(admin,new(){Confirmed=true,Action="internal_account_create"},Origin,default));using var zip=new ZipArchive(new MemoryStream(bytes));using var data=new MemoryStream();await zip.GetEntry("eventos.json")!.Open().CopyToAsync(data);
        using var manifest=JsonDocument.Parse(await new StreamReader(zip.GetEntry("manifest.json")!.Open()).ReadToEndAsync());Assert.Equal(Convert.ToHexString(SHA256.HashData(data.ToArray())),manifest.RootElement.GetProperty("Manifest").GetProperty("Sha256").GetString());Assert.Equal(2,await f.Db(db=>db.Events.CountAsync(e=>e.Action=="audit_export")));
    }
    [Theory][InlineData("content")][InlineData("middle")][InlineData("tail")][InlineData("head")]
    public async Task SYA004_IntegrityDetectsAlterationsAndMissingRows(string kind)
    {
        await Create();await f.Login();Assert.True((await Verify()).IsValid);
        await f.Db(async db=>{if(kind=="content")await db.Events.Where(e=>e.Action=="internal_account_create").ExecuteUpdateAsync(s=>s.SetProperty(e=>e.Detail,"Alterado"));if(kind=="middle")await db.Events.Where(e=>e.Action=="internal_account_create").ExecuteDeleteAsync();if(kind=="tail"){var id=await db.Events.MaxAsync(e=>e.Id);await db.Events.Where(e=>e.Id==id).ExecuteDeleteAsync();}if(kind=="head")await db.AuditHeads.ExecuteDeleteAsync();});Assert.False((await Verify()).IsValid);
    }
    [Fact]public async Task UpgradePreservesAccountsAndLegacyHistory()
    {
        await f.ResetAsync();var before=await f.Db(db=>db.Users.AsNoTracking().OrderBy(u=>u.Email).ToListAsync());
        await f.Db(async db=>{await db.GetService<IMigrator>().MigrateAsync("20260922172942_InitialSecurity");await db.Database.ExecuteSqlRawAsync("INSERT INTO security_events(occurred_at_utc,actor_role,action,outcome,origin,correlation_id,detail) VALUES(SYSUTCDATETIME(),N'Administrador','legacy_login',N'Exitoso','127.0.0.1','upgrade','Existing history')");await db.Database.MigrateAsync();await db.InitializeAuditAsync();});
        var after=await f.Db(db=>db.Users.AsNoTracking().OrderBy(u=>u.Email).ToListAsync());Assert.Equal(before.Select(u=>(u.Id,u.PasswordHash)),after.Select(u=>(u.Id,u.PasswordHash)));Assert.All(after,u=>{Assert.NotEqual(Guid.Empty,u.Revision);Assert.Equal(u.CreatedAtUtc,u.UpdatedAtUtc);});Assert.True(await f.Db(db=>db.Events.Where(e=>e.Action=="legacy_login").Select(e=>e.IsLegacy).SingleAsync()));admin=Actor(await f.Login("admin@ameli.test"));Assert.True((await Verify()).IsValid);
    }
}
