using System.ComponentModel.DataAnnotations;
namespace Ameli.Contracts;
public class InternalAccountRequest
{
    [Required(ErrorMessage="El nombre completo es obligatorio."), StringLength(160, MinimumLength=2)] public string Name { get; set; } = "";
    [Required(ErrorMessage="El correo es obligatorio."), EmailAddress(ErrorMessage="Escribe un correo válido."), MaxLength(254)] public string Email { get; set; } = "";
    [Required, RegularExpression("^[0-9]{8}$",ErrorMessage="El teléfono debe tener exactamente 8 dígitos.")] public string Phone { get; set; } = "";
    [Required, AllowedValues(Roles.Administrator,Roles.Logistics,ErrorMessage="Selecciona Administrador o Logística.")] public string Role { get; set; } = "";
    [Required] public bool? IsActive { get; set; }
}
public sealed class CreateInternalAccountRequest : InternalAccountRequest
{
    [Required(ErrorMessage="La contraseña inicial es obligatoria."), StringLength(64, MinimumLength=8, ErrorMessage="La contraseña debe tener entre 8 y 64 caracteres.")] public string Password { get; set; } = "";
    [Required(ErrorMessage="Confirma la contraseña inicial."), Compare(nameof(Password), ErrorMessage="Las contraseñas deben coincidir.")] public string ConfirmPassword { get; set; } = "";
}
public sealed class UpdateInternalAccountRequest : InternalAccountRequest
{
    [Required] public Guid? Revision { get; set; }
    [MaxLength(64)] public string? Password { get; set; }
    [MaxLength(64)] public string? ConfirmPassword { get; set; }
    [MaxLength(500)] public string Reason { get; set; } = "";
    public bool Confirmed { get; set; }
}
public sealed class InternalStateRequest
{
    [Required] public Guid? Revision { get; set; }
    [Required] public bool? IsActive { get; set; }
    [Required,StringLength(500,MinimumLength=5)] public string Reason { get; set; } = "";
    public bool Confirmed { get; set; }
}
public sealed class InternalAccountQuery
{
    [MaxLength(160)] public string? Search { get; set; }
    public bool? IsActive { get; set; }
    [Range(1,100000)] public int Page { get; set; } = 1;
    [Range(1,100)] public int PageSize { get; set; } = 20;
}
public sealed record InternalAccountView(Guid Id,string Name,string Email,string Phone,string Role,bool IsActive,DateTimeOffset CreatedAtUtc,DateTimeOffset UpdatedAtUtc,Guid Revision,DateTimeOffset? LockedUntilUtc);
public sealed record PagedResult<T>(List<T> Items,int Total,int Page,int PageSize) { public int TotalPages => Math.Max(1,(int)Math.Ceiling((double)Total/PageSize)); }
public class AuditQuery : IValidatableObject
{
    public DateOnly? From { get; set; } public DateOnly? To { get; set; }
    [MaxLength(254)] public string? User { get; set; }
    [MaxLength(30)] public string? Role { get; set; }
    [MaxLength(80)] public string? Module { get; set; }
    [MaxLength(80)] public string? Action { get; set; }
    [MaxLength(30)] public string? Outcome { get; set; }
    [MaxLength(80)] public string? Ip { get; set; }
    [Range(1,100000)] public int Page { get; set; } = 1;
    [Range(1,100)] public int PageSize { get; set; } = 20;
    public IEnumerable<ValidationResult> Validate(ValidationContext c)
    { if(From>To || To==DateOnly.MaxValue) yield return new("Revisa el intervalo de fechas.",[nameof(From),nameof(To)]); }
}
public sealed class AuditExportRequest : AuditQuery { public bool Confirmed { get; set; } }
public sealed record AuditEventView(long Id,DateTimeOffset OccurredAtUtc,Guid? ActorUserId,string ActorName,string ActorEmail,string ActorRole,string Module,string Action,string Entity,string EntityId,Guid? SubjectUserId,Guid? SessionId,string Outcome,string Origin,string CorrelationId,string Detail,string BeforeJson,string AfterJson,int? FailedAttempts,DateTimeOffset? LockoutStartedAtUtc,DateTimeOffset? LockedUntilUtc,bool IsLegacy,int IntegrityVersion,string PreviousHash,string IntegrityHash);
public sealed record IntegrityIssue(long? EventId,string Reason);
public sealed record IntegrityReport(bool IsValid,long CheckedRecords,int SelectedRecords,long? LastEventId,DateTimeOffset? BaselineAtUtc,List<IntegrityIssue> Issues,int IssueCount,string Message);
public sealed record WebAccessDeniedRequest([property:Required,MaxLength(250)] string Path);
