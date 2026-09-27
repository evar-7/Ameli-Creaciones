using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
using Ameli.Api.Domain;
using Ameli.Api.Infrastructure;
using Ameli.Contracts;
using Microsoft.EntityFrameworkCore;
namespace Ameli.Api.Application;
public sealed partial class SecurityService
{
    private IQueryable<SecurityEvent> Filter(AuditQuery q)
    {
        ValidateInput(q);var rows=db.Events.AsNoTracking();
        if(q.From.HasValue){var d=new DateTimeOffset(q.From.Value.ToDateTime(TimeOnly.MinValue),TimeSpan.Zero);rows=rows.Where(e=>e.OccurredAtUtc>=d);}
        if(q.To.HasValue){var d=new DateTimeOffset(q.To.Value.AddDays(1).ToDateTime(TimeOnly.MinValue),TimeSpan.Zero);rows=rows.Where(e=>e.OccurredAtUtc<d);}
        if(!string.IsNullOrWhiteSpace(q.User)){var text=q.User.Trim();Guid.TryParse(text,out var id);rows=rows.Where(e=>e.ActorName.Contains(text)||e.ActorEmail.Contains(text)||e.ActorUserId==id||e.SubjectUserId==id);}
        if(!string.IsNullOrWhiteSpace(q.Role))rows=rows.Where(e=>e.ActorRole==q.Role);
        if(!string.IsNullOrWhiteSpace(q.Module))rows=rows.Where(e=>e.Module==q.Module);
        if(!string.IsNullOrWhiteSpace(q.Action))rows=rows.Where(e=>e.Action==q.Action);
        if(!string.IsNullOrWhiteSpace(q.Outcome))rows=rows.Where(e=>e.Outcome==q.Outcome);
        if(!string.IsNullOrWhiteSpace(q.Ip))rows=rows.Where(e=>e.Origin==q.Ip);return rows;
    }
    private static AuditEventView EventView(SecurityEvent e)=>new(e.Id,e.OccurredAtUtc,e.ActorUserId,e.ActorName,e.ActorEmail,e.ActorRole,e.Module,e.Action,e.Entity,e.EntityId,e.SubjectUserId,e.SessionId,e.Outcome,e.Origin,e.CorrelationId,e.Detail,e.BeforeJson,e.AfterJson,e.FailedAttempts,e.LockoutStartedAtUtc,e.LockedUntilUtc,e.IsLegacy,e.IntegrityVersion,e.PreviousHash,e.IntegrityHash);
    private static string Filters(AuditQuery q)=>JsonSerializer.Serialize(new{q.From,q.To,q.User,q.Role,q.Module,q.Action,q.Outcome,q.Ip});
    public async Task<PagedResult<AuditEventView>> AuditEventsAsync(Actor actor,AuditQuery q,CancellationToken ct)
    {
        await RequireAdminAsync(actor,ct);var rows=Filter(q);var total=await rows.CountAsync(ct);
        var events=await rows.OrderByDescending(e=>e.OccurredAtUtc).ThenByDescending(e=>e.Id).Skip((q.Page-1)*q.PageSize).Take(q.PageSize).ToListAsync(ct);
        return new(events.Select(EventView).ToList(),total,q.Page,q.PageSize);
    }
    private Task<int> LockChain(CancellationToken ct)=>db.Database.ExecuteSqlInterpolatedAsync($"""
        DECLARE @r int; EXEC @r=sys.sp_getapplock @Resource={SecurityDbContext.AuditLock},@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=10000;
        IF @r<0 THROW 51000,'Audit transaction busy',1;
        """,ct);
    private void AuditOperation(string action,string outcome,AppUser user,Actor actor,RequestOrigin origin,string detail)
    {var e=Audit(action,outcome,origin,user,detail:detail);e.Module="Auditoría";e.Entity="security_events";e.EntityId="";e.SubjectUserId=null;e.SessionId=actor.SessionId;}
    public async Task<IntegrityReport> VerifyAuditAsync(Actor actor,AuditQuery q,RequestOrigin origin,CancellationToken ct)
    {
        await using var tx=await transactions.BeginAsync([SqlSecurityTransaction.User(actor.UserId)],ct);var admin=await RequireAdminAsync(actor,ct);await LockChain(ct);
        var selected=await Filter(q).CountAsync(ct);var head=await db.AuditHeads.AsNoTracking().SingleOrDefaultAsync(h=>h.Id==1,ct);
        List<IntegrityIssue> issues=[];int issueCount=0;long count=0,last=0;var previous="";
        void Issue(long? id,string text){issueCount++;if(issues.Count<100)issues.Add(new(id,text));}
        var headValid=head is not null&&head.KeyId==signer.KeyId&&AuditIntegrity.Matches(signer.HeadHash(head),head.Signature);
        if(!headValid)Issue(null,"Cabecera inexistente, alterada o clave incorrecta.");
        await foreach(var e in db.Events.AsNoTracking().OrderBy(e=>e.Id).AsAsyncEnumerable().WithCancellation(ct))
        {
            count++;last=e.Id;if(e.PreviousHash!=previous)Issue(e.Id,"Enlace roto: posible registro eliminado o insertado.");
            if(e.IntegrityVersion!=1||!AuditIntegrity.Matches(signer.EventHash(e),e.IntegrityHash))Issue(e.Id,"Contenido o firma alterados.");previous=e.IntegrityHash;
        }
        if(head is not null&&(head.LastEventId!=last||head.RecordCount!=count||head.LastHash!=previous))Issue(last,"La cantidad o el último registro no coincide con la cabecera firmada.");
        var message=issueCount==0?"Integridad verificada. Se comprobó la cadena completa para detectar también registros faltantes.":"Se detectaron inconsistencias. Conserva una copia y solicita revisión técnica.";
        if(headValid){AuditOperation("audit_integrity_check",issueCount==0?"Exitoso":"Fallido",admin,actor,origin,$"Registros: {count}; inconsistencias: {issueCount}; filtros: {Filters(q)}");await db.SaveChangesAsync(ct);}
        else {logger.LogError("Audit check cannot be appended: invalid head. Actor {ActorId}; correlation {Correlation}",actor.UserId,origin.CorrelationId);message+=" No se pudo anexar la comprobación a SQL: se emitió una alerta en el registro operativo.";}
        await tx.CommitAsync(ct);return new(issueCount==0,count,selected,last,head?.BaselineAtUtc,issues,issueCount,message);
    }
    public async Task<byte[]> ExportAuditAsync(Actor actor,AuditExportRequest q,RequestOrigin origin,CancellationToken ct)
    {
        await using var tx=await transactions.BeginAsync([SqlSecurityTransaction.User(actor.UserId)],ct);var admin=await RequireAdminAsync(actor,ct);
        try
        {
            ValidateInput(q);if(!q.Confirmed)throw new SecurityFault("confirmation_required","Confirma la exportación.");await LockChain(ct);
            var count=await Filter(q).CountAsync(ct);if(count>10000)throw new SecurityFault("export_limit","Hay más de 10 000 registros. Reduce el intervalo o agrega filtros.");
            var rows=await Filter(q).OrderBy(e=>e.Id).ToListAsync(ct);var data=JsonSerializer.SerializeToUtf8Bytes(rows.Select(EventView),new JsonSerializerOptions{WriteIndented=true});
            var head=await db.AuditHeads.AsNoTracking().SingleAsync(h=>h.Id==1,ct);var sha=Convert.ToHexString(SHA256.HashData(data));
            var manifest=JsonSerializer.Serialize(new{Format="Ameli.Audit.Export.v1",ExportedAtUtc=Now,ExporterId=admin.Id,ExporterName=admin.Name,Filters=JsonSerializer.Deserialize<JsonElement>(Filters(q)),Count=count,head.LastEventId,head.RecordCount,head.LastHash,head.Signature,head.KeyId,head.BaselineAtUtc,File="eventos.json",Sha256=sha});
            using var buffer=new MemoryStream();using(var zip=new ZipArchive(buffer,ZipArchiveMode.Create,true))
            {
                await using(var stream=zip.CreateEntry("eventos.json").Open())await stream.WriteAsync(data,ct);
                await using(var writer=new StreamWriter(zip.CreateEntry("manifest.json").Open(),new UTF8Encoding(false)))await writer.WriteAsync(JsonSerializer.Serialize(new{Manifest=JsonSerializer.Deserialize<JsonElement>(manifest),CanonicalManifest=manifest,HmacSha256=signer.Sign(manifest)}).AsMemory(),ct);
            }
            AuditOperation("audit_export","Exitoso",admin,actor,origin,$"Registros: {count}; SHA256: {sha}; filtros: {Filters(q)}");await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);return buffer.ToArray();
        }
        catch(SecurityFault e){AuditOperation("audit_export","Rechazado",admin,actor,origin,e.Code);await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);throw;}
    }
    public async Task WebDeniedAsync(Actor actor,string path,RequestOrigin origin,CancellationToken ct)
    {var (user,_)=await RequireActorAsync(actor,ct);var e=Audit("web_access_denied","Rechazado",origin,user,detail:path.Split('?')[0]);e.SessionId=actor.SessionId;e.Entity="route";e.EntityId="";e.SubjectUserId=null;await db.SaveChangesAsync(ct);}
}
