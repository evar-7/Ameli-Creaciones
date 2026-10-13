using System.Buffers.Binary;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ameli.Api.Domain;
using Ameli.Api.Infrastructure;
using Ameli.Contracts;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Ameli.Api.Application;

public sealed class CustomerIdentityService(SecurityDbContext db, SqlSecurityTransaction transactions,
    IPasswordHasher<AppUser> passwords, TimeProvider clock)
{
    private DateTimeOffset Now => clock.GetUtcNow();
    private static SecurityFault Missing(Dictionary<string, string[]> errors) =>
        new("required_fields", "Campos obligatorios incompletos") { Errors = errors };
    private static SecurityFault Invalid(Dictionary<string, string[]> errors, string message = "Revisa los datos ingresados.") =>
        new("invalid_fields", message) { Errors = errors };

    private static void Required(Dictionary<string, string[]> errors, string key, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) errors[key] = ["Este campo es obligatorio."];
    }

    public async Task RegisterAsync(CustomerRegistrationRequest request, RequestOrigin origin, CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        Required(errors, nameof(request.FirstName), request.FirstName);
        Required(errors, nameof(request.LastNames), request.LastNames);
        Required(errors, nameof(request.Email), request.Email);
        Required(errors, nameof(request.Phone), request.Phone);
        Required(errors, nameof(request.Password), request.Password);
        Required(errors, nameof(request.ConfirmPassword), request.ConfirmPassword);
        if (errors.Count > 0) throw Missing(errors);

        var first = request.FirstName.Trim(); var last = request.LastNames.Trim();
        var email = request.Email.Trim(); var phone = request.Phone.Trim();
        if (first.Length is < 2 or > 80) errors[nameof(request.FirstName)] = ["Escribe entre 2 y 80 caracteres."];
        if (last.Length is < 2 or > 160) errors[nameof(request.LastNames)] = ["Escribe entre 2 y 160 caracteres."];
        if ($"{first} {last}".Length > 160) errors[nameof(request.LastNames)] = ["Nombre y apellidos juntos no deben superar 160 caracteres."];
        if (email.Length > 254 || !new EmailAddressAttribute().IsValid(email)) errors[nameof(request.Email)] = ["Escribe un correo válido."];
        if (phone.Length != 8 || !phone.All(char.IsAsciiDigit)) errors[nameof(request.Phone)] = ["El teléfono debe tener exactamente 8 dígitos."];
        var unmet = PasswordPolicy.UnmetRequirements(request.Password);
        if (unmet.Length > 0) errors[nameof(request.Password)] = unmet;
        if (request.Password != request.ConfirmPassword) errors[nameof(request.ConfirmPassword)] = ["Las contraseñas deben coincidir."];
        if (errors.Count > 0) throw Invalid(errors);

        var normalized = SecurityService.NormalizeEmail(email);
        var lockName = "ameli:register:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
        await using var tx = await transactions.BeginAsync([lockName], ct);
        if (await db.Users.AnyAsync(x => x.NormalizedEmail == normalized, ct))
            throw new SecurityFault("duplicate_email", "Correo electronico ya utilizado, intenta con otro correo", 409)
            { Errors = new() { [nameof(request.Email)] = ["Correo electronico ya utilizado, intenta con otro correo"] } };
        var user = new AppUser { FirstName = first, LastNames = last, Name = $"{first} {last}",
            Email = email, NormalizedEmail = normalized, Phone = phone, RoleName = Roles.Client,
            IsInternal = false, IsActive = true, CreatedAtUtc = Now, UpdatedAtUtc = Now };
        user.PasswordHash = passwords.HashPassword(user, request.Password);
        db.Users.Add(user);
        Audit("customer_register", "users", user, origin);
        try { await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            throw new SecurityFault("duplicate_email", "Correo electronico ya utilizado, intenta con otro correo", 409)
            { Errors = new() { [nameof(request.Email)] = ["Correo electronico ya utilizado, intenta con otro correo"] } };
        }
    }

    private async Task<AppUser> RequireClientAsync(Actor actor, CancellationToken ct) =>
        await db.Users.SingleAsync(x => x.Id == actor.UserId && x.IsActive && x.RoleName == Roles.Client, ct);

    private SecurityEvent Audit(string action, string entity, AppUser owner, RequestOrigin origin, Guid? target = null)
    {
        var e = new SecurityEvent { OccurredAtUtc = Now, ActorUserId = owner.Id, SubjectUserId = owner.Id,
            ActorName = owner.Name, ActorEmail = owner.Email, ActorRole = owner.RoleName, Action = action,
            Outcome = "Exitoso", Module = "Gestión de identidad", Entity = entity, EntityId = (target ?? owner.Id).ToString(),
            Origin = origin.Ip, CorrelationId = origin.CorrelationId };
        db.Events.Add(e); return e;
    }

    private static PetView View(PetProfile p) => new(p.Id, p.Name, p.Species, p.Breed, p.AgeYears,
        p.WeightKg, p.HeightCm, p.Allergies, p.IsActive, p.PhotoContentType is not null,
        p.CreatedAtUtc, p.UpdatedAtUtc, p.Revision);

    public async Task<List<PetView>> PetsAsync(Actor actor, CancellationToken ct)
    {
        await RequireClientAsync(actor, ct);
        return await db.Pets.AsNoTracking().Where(x => x.OwnerUserId == actor.UserId)
            .OrderByDescending(x => x.IsActive).ThenBy(x => x.Name)
            .Select(x => new PetView(x.Id, x.Name, x.Species, x.Breed, x.AgeYears, x.WeightKg,
                x.HeightCm, x.Allergies, x.IsActive, x.PhotoContentType != null,
                x.CreatedAtUtc, x.UpdatedAtUtc, x.Revision)).ToListAsync(ct);
    }

    public async Task<PetView> PetAsync(Actor actor, Guid id, CancellationToken ct)
    {
        await RequireClientAsync(actor, ct);
        return View(await OwnedPetAsync(actor, id, ct));
    }

    private async Task<PetProfile> OwnedPetAsync(Actor actor, Guid id, CancellationToken ct) =>
        await db.Pets.SingleOrDefaultAsync(x => x.Id == id && x.OwnerUserId == actor.UserId, ct)
        ?? throw new SecurityFault("pet_not_found", "Mascota no encontrada.", 404);

    private static (string name, string species, string breed, int age, decimal weight, decimal height, string allergies)
        ValidatePet(PetRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        Required(errors, nameof(request.Name), request.Name);
        Required(errors, nameof(request.Species), request.Species);
        Required(errors, nameof(request.Breed), request.Breed);
        Required(errors, nameof(request.AgeYears), request.AgeYears);
        Required(errors, nameof(request.WeightKg), request.WeightKg);
        Required(errors, nameof(request.HeightCm), request.HeightCm);
        if (errors.Count > 0) throw Missing(errors);
        var name = request.Name.Trim(); var species = request.Species.Trim(); var breed = request.Breed.Trim();
        var allergies = request.Allergies?.Trim() ?? "";
        if (name.Length > 100) errors[nameof(request.Name)] = ["Máximo 100 caracteres."];
        if (species.Length > 40) errors[nameof(request.Species)] = ["Máximo 40 caracteres."];
        if (breed.Length > 100) errors[nameof(request.Breed)] = ["Máximo 100 caracteres."];
        if (allergies.Length > 500) errors[nameof(request.Allergies)] = ["Máximo 500 caracteres."];
        if (!int.TryParse(request.AgeYears, NumberStyles.None, CultureInfo.InvariantCulture, out var age) || age > 40)
            errors[nameof(request.AgeYears)] = ["La edad debe ser un número de años entre 0 y 40."];
        const NumberStyles numeric = NumberStyles.AllowDecimalPoint;
        var weightValid = decimal.TryParse(request.WeightKg, numeric, CultureInfo.InvariantCulture, out var weight) && weight is > 0 and <= 200;
        var heightValid = decimal.TryParse(request.HeightCm, numeric, CultureInfo.InvariantCulture, out var height) && height is > 0 and <= 250;
        if (!weightValid || decimal.Round(weight, 2) != weight)
            errors[nameof(request.WeightKg)] = ["No se permiten valores negativos y en peso deben ser estrictamente kg. Escribe un número mayor que 0 y hasta 200 kg (máximo 2 decimales)."];
        if (!heightValid || decimal.Round(height, 2) != height)
            errors[nameof(request.HeightCm)] = ["La talla debe ser un número mayor que 0 y hasta 250 cm (máximo 2 decimales)."];
        if (errors.Count > 0) throw Invalid(errors, "No se permiten valores negativos y en peso deben ser estrictamente kg.");
        return (name, species, breed, age, weight, height, allergies);
    }

    public async Task<PetView> CreatePetAsync(Actor actor, PetRequest request, RequestOrigin origin, CancellationToken ct)
    {
        var data = ValidatePet(request);
        await using var tx = await transactions.BeginAsync([SqlSecurityTransaction.User(actor.UserId)], ct);
        var owner = await RequireClientAsync(actor, ct);
        var pet = new PetProfile { OwnerUserId = actor.UserId, Name = data.name, Species = data.species,
            Breed = data.breed, AgeYears = data.age, WeightKg = data.weight, HeightCm = data.height,
            Allergies = data.allergies, CreatedAtUtc = Now, UpdatedAtUtc = Now };
        db.Pets.Add(pet); Audit("pet_create", "pet_profiles", owner, origin, pet.Id);
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return View(pet);
    }

    public async Task<PetChangeResult> UpdatePetAsync(Actor actor, Guid id, PetRequest request, RequestOrigin origin, CancellationToken ct)
    {
        var data = ValidatePet(request);
        var hasPhoto = request.PhotoFileName is not null || request.PhotoBase64Data is not null;
        (byte[] bytes, string contentType)? photo = hasPhoto ? ValidatePhoto(new PetPhotoRequest
        { FileName = request.PhotoFileName ?? "", Base64Data = request.PhotoBase64Data ?? "" }) : null;
        await using var tx = await transactions.BeginAsync([SqlSecurityTransaction.User(actor.UserId)], ct);
        var owner = await RequireClientAsync(actor, ct);
        var pet = await OwnedPetAsync(actor, id, ct);
        if (request.Revision != pet.Revision) throw new SecurityFault("stale_pet", "La ficha cambió. Recarga y revisa los datos vigentes.", 409);
        var active = request.IsActive ?? pet.IsActive;
        var photoChanged = photo is not null && (pet.PhotoContentType != photo.Value.contentType ||
            pet.Photo is null || !pet.Photo.AsSpan().SequenceEqual(photo.Value.bytes));
        if (pet.Name == data.name && pet.Species == data.species && pet.Breed == data.breed &&
            pet.AgeYears == data.age && pet.WeightKg == data.weight && pet.HeightCm == data.height &&
            pet.Allergies == data.allergies && pet.IsActive == active && !photoChanged)
        { await tx.CommitAsync(ct); return new(View(pet), false, "No existen cambios por aplicar"); }
        pet.Name = data.name; pet.Species = data.species; pet.Breed = data.breed;
        pet.AgeYears = data.age; pet.WeightKg = data.weight; pet.HeightCm = data.height; pet.Allergies = data.allergies;
        pet.IsActive = active;
        if (photoChanged && photo is { } selectedPhoto)
        { pet.Photo = selectedPhoto.bytes; pet.PhotoContentType = selectedPhoto.contentType; }
        // A newly declared allergy overrides any earlier material choice.
        var materials = ReadArray(pet.MaterialsJson).Where(m => !Conflicts(m, data.allergies)).ToArray();
        if (materials.Length != ReadArray(pet.MaterialsJson).Length)
        { pet.MaterialsJson = JsonSerializer.Serialize(materials); pet.PreferencesUpdatedAtUtc = Now; }
        pet.UpdatedAtUtc = Now; pet.Revision = Guid.NewGuid(); Audit("pet_update", "pet_profiles", owner, origin, id);
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        return new(View(pet), true, "Perfil actualizado.");
    }

    public async Task<PetChangeResult> SetPetActiveAsync(Actor actor, Guid id, bool active, RequestOrigin origin, CancellationToken ct)
    {
        await using var tx = await transactions.BeginAsync([SqlSecurityTransaction.User(actor.UserId)], ct);
        var owner = await RequireClientAsync(actor, ct);
        var pet = await OwnedPetAsync(actor, id, ct);
        if (pet.IsActive == active) { await tx.CommitAsync(ct); return new(View(pet), false, "No existen cambios por aplicar"); }
        if (active && (pet.WeightKg <= 0 || pet.HeightCm <= 0 || pet.AgeYears is < 0 or > 40))
            throw new SecurityFault("invalid_pet", "Actualiza primero las medidas de esta mascota.");
        pet.IsActive = active; pet.UpdatedAtUtc = Now; pet.Revision = Guid.NewGuid();
        Audit(active ? "pet_reactivate" : "pet_deactivate", "pet_profiles", owner, origin, id);
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        return new(View(pet), true, active ? "Perfil reactivado." : "Perfil desactivado.");
    }

    private static string? PhotoType(byte[] bytes)
    {
        if (bytes.Length >= 20 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF &&
            bytes[^2] == 0xFF && bytes[^1] == 0xD9) return "image/jpeg";
        if (bytes.Length >= 45 && bytes.AsSpan().StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }) &&
            bytes.AsSpan(12, 4).SequenceEqual("IHDR"u8) && bytes.AsSpan(bytes.Length - 8, 4).SequenceEqual("IEND"u8))
            return "image/png";
        if (bytes.Length >= 20 && bytes.AsSpan(0, 4).SequenceEqual("RIFF"u8) &&
            bytes.AsSpan(8, 4).SequenceEqual("WEBP"u8) &&
            BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4, 4)) + 8L == bytes.Length &&
            (bytes.AsSpan(12, 4).SequenceEqual("VP8 "u8) || bytes.AsSpan(12, 4).SequenceEqual("VP8L"u8) ||
             bytes.AsSpan(12, 4).SequenceEqual("VP8X"u8))) return "image/webp";
        return null;
    }

    private static (byte[] bytes, string contentType) ValidatePhoto(PetPhotoRequest request)
    {
        const int maxBytes = 5 * 1024 * 1024;
        if (request.Base64Data?.Length > ((maxBytes + 2) / 3) * 4 + 8)
            throw new SecurityFault("large_image", "No se admiten imagenes superiores a 5 MB")
            { Errors = new() { ["Photo"] = ["No se admiten imagenes superiores a 5 MB"] } };
        byte[] bytes;
        try { bytes = Convert.FromBase64String(request.Base64Data ?? ""); }
        catch (FormatException) { bytes = []; }
        if (bytes.Length > maxBytes)
            throw new SecurityFault("large_image", "No se admiten imagenes superiores a 5 MB")
            { Errors = new() { ["Photo"] = ["No se admiten imagenes superiores a 5 MB"] } };
        var kind = PhotoType(bytes);
        var ext = Path.GetExtension(request.FileName ?? "").ToLowerInvariant();
        if (kind is null || !((kind == "image/jpeg" && ext is ".jpg" or ".jpeg") ||
            (kind == "image/png" && ext == ".png") || (kind == "image/webp" && ext == ".webp")))
            throw new SecurityFault("invalid_image", "Formato de imagen no valida solo se adminten imagenes en formato JPG, PNG o WEBP")
            { Errors = new() { ["Photo"] = ["Formato de imagen no valida solo se adminten imagenes en formato JPG, PNG o WEBP"] } };
        return (bytes, kind);
    }

    public async Task<PetChangeResult> SavePhotoAsync(Actor actor, Guid id, PetPhotoRequest request, RequestOrigin origin, CancellationToken ct)
    {
        var (bytes, kind) = ValidatePhoto(request);
        await using var tx = await transactions.BeginAsync([SqlSecurityTransaction.User(actor.UserId)], ct);
        var owner = await RequireClientAsync(actor, ct);
        var pet = await OwnedPetAsync(actor, id, ct);
        if (pet.PhotoContentType == kind && pet.Photo is not null && pet.Photo.AsSpan().SequenceEqual(bytes))
        { await tx.CommitAsync(ct); return new(View(pet), false, "No existen cambios por aplicar"); }
        pet.Photo = bytes; pet.PhotoContentType = kind; pet.UpdatedAtUtc = Now; pet.Revision = Guid.NewGuid();
        Audit("pet_photo_update", "pet_profiles", owner, origin, id);
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return new(View(pet), true, "Imagen guardada.");
    }

    public async Task<PetPhotoPayload> PhotoAsync(Actor actor, Guid id, CancellationToken ct)
    {
        await RequireClientAsync(actor, ct);
        var pet = await OwnedPetAsync(actor, id, ct);
        if (pet.Photo is null || pet.PhotoContentType is null) throw new SecurityFault("photo_not_found", "Imagen no encontrada.", 404);
        return new(pet.PhotoContentType, pet.Photo);
    }

    private static string[] ReadArray(string json) => JsonSerializer.Deserialize<string[]>(json) ?? [];
    private static string Fold(string s)
    {
        var decomposed = s.ToLowerInvariant().Normalize(NormalizationForm.FormD);
        return new string(decomposed.Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray())
            .Normalize(NormalizationForm.FormC);
    }
    private static bool Conflicts(string material, string allergies) => allergies
        .Split([',', ';', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(Fold).Any(a => a == Fold(material) || a.Contains(Fold(material), StringComparison.Ordinal));

    private static string[] CleanChoices(string[]? choices, string field, Dictionary<string, string[]> errors)
    {
        if (choices is null) { errors[field] = ["Selecciona valores válidos."]; return []; }
        var result = choices.Select(s => s?.Trim() ?? "").Where(s => s.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (result.Length > 12 || result.Any(s => s.Length > 40)) errors[field] = ["Máximo 12 opciones de 40 caracteres."];
        return result;
    }

    public async Task<PetPreferencesView> PreferencesAsync(Actor actor, Guid id, CancellationToken ct)
    {
        await RequireClientAsync(actor, ct);
        return PreferenceView(await OwnedPetAsync(actor, id, ct));
    }

    private static PetPreferencesView PreferenceView(PetProfile pet) => new(ReadArray(pet.ColorsJson),
        ReadArray(pet.MaterialsJson), ReadArray(pet.GarmentTypesJson), ReadArray(pet.StylesJson), pet.PreferencesUpdatedAtUtc);

    public async Task<PetPreferencesView> SavePreferencesAsync(Actor actor, Guid id, PetPreferencesRequest request,
        RequestOrigin origin, CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        var colors = CleanChoices(request.Colors, nameof(request.Colors), errors);
        var materials = CleanChoices(request.Materials, nameof(request.Materials), errors);
        var garments = CleanChoices(request.GarmentTypes, nameof(request.GarmentTypes), errors);
        var styles = CleanChoices(request.Styles, nameof(request.Styles), errors);
        if (errors.Count > 0) throw Invalid(errors);
        await using var tx = await transactions.BeginAsync([SqlSecurityTransaction.User(actor.UserId)], ct);
        var owner = await RequireClientAsync(actor, ct);
        var pet = await OwnedPetAsync(actor, id, ct);
        var conflicting = materials.Where(x => Conflicts(x, pet.Allergies)).ToArray();
        if (conflicting.Length > 0) throw new SecurityFault("allergic_material", "Material alergico para tu mascota")
        { Errors = new() { [nameof(request.Materials)] = ["Material alergico para tu mascota: " + string.Join(", ", conflicting)] } };
        var next = new[] { JsonSerializer.Serialize(colors), JsonSerializer.Serialize(materials),
            JsonSerializer.Serialize(garments), JsonSerializer.Serialize(styles) };
        if (pet.ColorsJson == next[0] && pet.MaterialsJson == next[1] && pet.GarmentTypesJson == next[2] && pet.StylesJson == next[3])
        { await tx.CommitAsync(ct); return PreferenceView(pet); }
        pet.ColorsJson = next[0]; pet.MaterialsJson = next[1]; pet.GarmentTypesJson = next[2]; pet.StylesJson = next[3];
        pet.PreferencesUpdatedAtUtc = Now; pet.UpdatedAtUtc = Now; pet.Revision = Guid.NewGuid();
        Audit("pet_preferences_update", "pet_profiles", owner, origin, id);
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return PreferenceView(pet);
    }

    private static DeliveryAddressView View(DeliveryAddress a) => new(a.Id, a.Province, a.Canton,
        a.District, a.ExactAddress, a.Reference, a.IsDefault, a.CreatedAtUtc);

    public async Task<List<DeliveryAddressView>> AddressesAsync(Actor actor, CancellationToken ct)
    {
        await RequireClientAsync(actor, ct);
        return (await db.DeliveryAddresses.AsNoTracking().Where(x => x.OwnerUserId == actor.UserId)
            .OrderByDescending(x => x.IsDefault).ThenBy(x => x.CreatedAtUtc).ToListAsync(ct)).Select(View).ToList();
    }

    public async Task<DeliveryAddressView> AddAddressAsync(Actor actor, DeliveryAddressRequest request,
        RequestOrigin origin, CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        Required(errors, nameof(request.Province), request.Province);
        Required(errors, nameof(request.Canton), request.Canton);
        Required(errors, nameof(request.District), request.District);
        Required(errors, nameof(request.ExactAddress), request.ExactAddress);
        Required(errors, nameof(request.Reference), request.Reference);
        if (errors.Count > 0) throw Missing(errors);
        var values = new[] { request.Province.Trim(), request.Canton.Trim(), request.District.Trim(),
            request.ExactAddress.Trim(), request.Reference.Trim() };
        var fields = new[] { nameof(request.Province), nameof(request.Canton), nameof(request.District),
            nameof(request.ExactAddress), nameof(request.Reference) };
        for (var i = 0; i < values.Length; i++)
            if (values[i].Length > (i < 3 ? 100 : 300)) errors[fields[i]] = ["El texto es demasiado largo."];
        if (errors.Count > 0) throw Invalid(errors);

        await using var tx = await transactions.BeginAsync([SqlSecurityTransaction.User(actor.UserId)], ct);
        var owner = await RequireClientAsync(actor, ct);
        var existing = await db.DeliveryAddresses.Where(a => a.OwnerUserId == actor.UserId).ToListAsync(ct);
        var makeDefault = request.IsDefault || existing.Count == 0;
        if (makeDefault) foreach (var item in existing) item.IsDefault = false;
        var address = new DeliveryAddress { OwnerUserId = actor.UserId, Province = values[0], Canton = values[1],
            District = values[2], ExactAddress = values[3], Reference = values[4], IsDefault = makeDefault, CreatedAtUtc = Now };
        db.DeliveryAddresses.Add(address); Audit("address_create", "delivery_addresses", owner, origin, address.Id);
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return View(address);
    }

    public async Task<DeliveryAddressView> SetDefaultAddressAsync(Actor actor, Guid id, RequestOrigin origin, CancellationToken ct)
    {
        await using var tx = await transactions.BeginAsync([SqlSecurityTransaction.User(actor.UserId)], ct);
        var owner = await RequireClientAsync(actor, ct);
        var addresses = await db.DeliveryAddresses.Where(a => a.OwnerUserId == actor.UserId).ToListAsync(ct);
        var target = addresses.SingleOrDefault(a => a.Id == id)
            ?? throw new SecurityFault("address_not_found", "Dirección no encontrada.", 404);
        if (target.IsDefault) { await tx.CommitAsync(ct); return View(target); }
        foreach (var a in addresses) a.IsDefault = a.Id == id;
        Audit("address_default", "delivery_addresses", owner, origin, id);
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); return View(target);
    }

    public async Task DeleteAddressAsync(Actor actor, Guid id, RequestOrigin origin, CancellationToken ct)
    {
        await using var tx = await transactions.BeginAsync([SqlSecurityTransaction.User(actor.UserId)], ct);
        var owner = await RequireClientAsync(actor, ct);
        var addresses = await db.DeliveryAddresses.Where(a => a.OwnerUserId == actor.UserId).OrderBy(a => a.CreatedAtUtc).ToListAsync(ct);
        var target = addresses.SingleOrDefault(a => a.Id == id)
            ?? throw new SecurityFault("address_not_found", "Dirección no encontrada.", 404);
        db.DeliveryAddresses.Remove(target);
        if (target.IsDefault) { var next = addresses.FirstOrDefault(a => a.Id != id); if (next is not null) next.IsDefault = true; }
        Audit("address_delete", "delivery_addresses", owner, origin, id);
        await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
    }
}
