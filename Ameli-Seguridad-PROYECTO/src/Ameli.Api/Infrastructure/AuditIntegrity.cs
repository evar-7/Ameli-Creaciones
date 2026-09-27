using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ameli.Api.Domain;
namespace Ameli.Api.Infrastructure;
public sealed class AuditIntegrity
{
    private readonly byte[] key;
    public string KeyId { get; }
    public AuditIntegrity(IConfiguration config)
    {
        key=Encoding.UTF8.GetBytes(config["Audit:IntegrityKey"]??"");
        if(key.Length<32) throw new InvalidOperationException("Configura Audit:IntegrityKey con al menos 32 bytes aleatorios. En Development se genera en App_Data; conserva siempre la misma clave.");
        KeyId=Convert.ToHexString(SHA256.HashData(key))[..16];
    }
    private static string? Stamp(DateTimeOffset? d)=>d?.ToUniversalTime().ToString("O",CultureInfo.InvariantCulture);
    public string Sign(string text)=>Convert.ToHexString(HMACSHA256.HashData(key,Encoding.UTF8.GetBytes(text)));
    public string EventHash(SecurityEvent e)=>Sign(JsonSerializer.Serialize(new object?[]{"Ameli.Audit.Event.v1",e.Id,Stamp(e.OccurredAtUtc),e.ActorUserId,e.SubjectUserId,e.ActorName,e.ActorEmail,e.ActorRole,e.Module,e.Action,e.Entity,e.EntityId,e.SessionId,e.Outcome,e.Origin,e.CorrelationId,e.Detail,e.BeforeJson,e.AfterJson,e.FailedAttempts,Stamp(e.LockoutStartedAtUtc),Stamp(e.LockedUntilUtc),e.IntegrityVersion,e.IsLegacy,e.PreviousHash}));
    public string HeadHash(AuditChainHead h)=>Sign(JsonSerializer.Serialize(new object?[]{"Ameli.Audit.Head.v1",h.Id,h.LastEventId,h.RecordCount,h.LastHash,h.KeyId,Stamp(h.BaselineAtUtc)}));
    public static bool Matches(string expected,string actual) { try { return CryptographicOperations.FixedTimeEquals(Convert.FromHexString(expected),Convert.FromHexString(actual)); } catch(FormatException) { return false; } }
}
