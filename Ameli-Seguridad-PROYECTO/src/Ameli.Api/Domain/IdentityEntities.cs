namespace Ameli.Api.Domain;

public sealed class PetProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OwnerUserId { get; set; }
    public string Name { get; set; } = "";
    public string Species { get; set; } = "";
    public string Breed { get; set; } = "";
    public int AgeYears { get; set; }
    public decimal WeightKg { get; set; }
    public decimal HeightCm { get; set; }
    public string Allergies { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public byte[]? Photo { get; set; }
    public string? PhotoContentType { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public Guid Revision { get; set; } = Guid.NewGuid();
    public string ColorsJson { get; set; } = "[]";
    public string MaterialsJson { get; set; } = "[]";
    public string GarmentTypesJson { get; set; } = "[]";
    public string StylesJson { get; set; } = "[]";
    public DateTimeOffset? PreferencesUpdatedAtUtc { get; set; }
}

public sealed class DeliveryAddress
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OwnerUserId { get; set; }
    public string Province { get; set; } = "";
    public string Canton { get; set; } = "";
    public string District { get; set; } = "";
    public string ExactAddress { get; set; } = "";
    public string Reference { get; set; } = "";
    public bool IsDefault { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
}
