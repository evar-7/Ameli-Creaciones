using Ameli.Api.Domain;
using Microsoft.EntityFrameworkCore;
namespace Ameli.Api.Infrastructure;
public sealed partial class SecurityDbContext
{
    public const string AuditLock="ameli:audit-chain";
    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        if(ChangeTracker.Entries<SecurityEvent>().Any(e=>e.State!=EntityState.Unchanged)) throw new InvalidOperationException("Usa SaveChangesAsync para las operaciones auditadas.");
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }
    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess,CancellationToken ct=default)
    {
        var entries=ChangeTracker.Entries<SecurityEvent>().ToArray();
        if(entries.Any(e=>e.State is EntityState.Modified or EntityState.Deleted)) throw new InvalidOperationException("La auditoría no se puede editar ni eliminar desde la aplicación.");
        var added=entries.Where(e=>e.State==EntityState.Added).Select(e=>e.Entity).ToArray();
        if(added.Length==0) return await base.SaveChangesAsync(acceptAllChangesOnSuccess,ct);
        if(!acceptAllChangesOnSuccess) throw new InvalidOperationException("La escritura auditada requiere aceptar los cambios.");
        var signer=integrity??throw new InvalidOperationException("Falta la clave de auditoría.");
        await using var owned=Database.CurrentTransaction is null?await Database.BeginTransactionAsync(ct):null;
        await LockAuditAsync(ct); var head=await LoadHeadAsync(signer,ct);
        foreach(var e in added){e.IntegrityVersion=1;e.IsLegacy=false;}
        var result=await base.SaveChangesAsync(true,ct); // SQL assigns identities inside the uncommitted transaction.
        foreach(var e in added.OrderBy(e=>e.Id)) { e.PreviousHash=head.LastHash;e.IntegrityHash=signer.EventHash(e);head.LastHash=e.IntegrityHash;head.LastEventId=e.Id;head.RecordCount++; }
        head.Signature=signer.HeadHash(head); await base.SaveChangesAsync(true,ct);
        if(owned is not null) await owned.CommitAsync(ct); AuditRecorded=true;return result;
    }
    public async Task InitializeAuditAsync(CancellationToken ct=default)
    {
        await using var owned=Database.CurrentTransaction is null?await Database.BeginTransactionAsync(ct):null;
        await LockAuditAsync(ct);await LoadHeadAsync(integrity??throw new InvalidOperationException("Falta la clave de auditoría."),ct);
        await base.SaveChangesAsync(true,ct);if(owned is not null) await owned.CommitAsync(ct);
    }
    private Task<int> LockAuditAsync(CancellationToken ct)=>Database.ExecuteSqlInterpolatedAsync($"""
        DECLARE @r int; EXEC @r=sys.sp_getapplock @Resource={AuditLock},@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=10000;
        IF @r<0 THROW 51000,'Audit transaction busy',1;
        """,ct);
    private async Task<AuditChainHead> LoadHeadAsync(AuditIntegrity signer,CancellationToken ct)
    {
        foreach(var e in ChangeTracker.Entries<AuditChainHead>().ToArray()) e.State=EntityState.Detached;
        var head=await AuditHeads.SingleOrDefaultAsync(h=>h.Id==1,ct);
        if(head is not null)
        {
            if(head.KeyId!=signer.KeyId || !AuditIntegrity.Matches(signer.HeadHash(head),head.Signature)) throw new InvalidOperationException("Cabecera de auditoría alterada o clave incorrecta. Restaura la clave original; la cadena no se reiniciará.");
            return head;
        }
        if(await Events.AnyAsync(e=>e.IntegrityVersion!=0,ct)) throw new InvalidOperationException("Falta la cabecera de una auditoría ya firmada.");
        head=new(){KeyId=signer.KeyId,BaselineAtUtc=DateTimeOffset.UtcNow};
        foreach(var e in await Events.OrderBy(e=>e.Id).ToListAsync(ct))
        { e.IsLegacy=true;e.IntegrityVersion=1;e.PreviousHash=head.LastHash;e.IntegrityHash=signer.EventHash(e);head.LastHash=e.IntegrityHash;head.LastEventId=e.Id;head.RecordCount++; }
        head.Signature=signer.HeadHash(head);AuditHeads.Add(head);return head;
    }
}
