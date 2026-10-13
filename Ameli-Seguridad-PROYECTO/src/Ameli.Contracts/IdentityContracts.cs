namespace Ameli.Contracts;

public sealed class CustomerRegistrationRequest
{
    public string FirstName { get; set; } = "";
    public string LastNames { get; set; } = "";
    public string Email { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Password { get; set; } = "";
    public string ConfirmPassword { get; set; } = "";
}

// Numeric fields arrive as text so even malformed units and non-numeric input
// receive field-specific validation instead of a model-binding 400 response.
public sealed class PetRequest
{
    public string Name { get; set; } = "";
    public string Species { get; set; } = "";
    public string Breed { get; set; } = "";
    public string AgeYears { get; set; } = "";
    public string WeightKg { get; set; } = "";
    public string HeightCm { get; set; } = "";
    public string Allergies { get; set; } = "";
    public bool? AllergiesDeclared { get; set; }
    public Guid? Revision { get; set; }
    // On edit these fields allow one Save changes operation for biometrics, photo and state.
    public bool? IsActive { get; set; }
    public string? PhotoFileName { get; set; }
    public string? PhotoBase64Data { get; set; }
}

public sealed class PetPhotoRequest
{
    public string FileName { get; set; } = "";
    public string Base64Data { get; set; } = "";
}

public sealed record PetPhotoPayload(string ContentType, byte[] Content);

public sealed record PetView(Guid Id, string Name, string Species, string Breed, int AgeYears,
    decimal WeightKg, decimal HeightCm, string Allergies, bool IsActive, bool HasPhoto,
    DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc, Guid Revision, bool AllergiesDeclared = false);

public sealed record PetChangeResult(PetView Pet, bool Changed, string Message);

public sealed class PetPreferencesRequest
{
    public string[] Colors { get; set; } = [];
    public string[] Materials { get; set; } = [];
    public string[] GarmentTypes { get; set; } = [];
    public string[] Styles { get; set; } = [];
}

public sealed record PetPreferencesView(string[] Colors, string[] Materials, string[] GarmentTypes, string[] Styles,
    DateTimeOffset? UpdatedAtUtc);

public sealed class DeliveryAddressRequest
{
    public string Province { get; set; } = "";
    public string Canton { get; set; } = "";
    public string District { get; set; } = "";
    public string ExactAddress { get; set; } = "";
    public string Reference { get; set; } = "";
    public bool IsDefault { get; set; }
}

public sealed record DeliveryAddressView(Guid Id, string Province, string Canton, string District,
    string ExactAddress, string Reference, bool IsDefault, DateTimeOffset CreatedAtUtc);
