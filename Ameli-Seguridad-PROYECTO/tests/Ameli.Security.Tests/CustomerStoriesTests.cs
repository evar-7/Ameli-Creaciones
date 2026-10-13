using System.Net;
using System.Net.Http.Json;
using Ameli.Api.Application;
using Ameli.Contracts;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static Ameli.Security.Tests.SecurityFixture;

namespace Ameli.Security.Tests;

public sealed class CustomerStoriesTests(SecurityFixture f) : IClassFixture<SecurityFixture>, IAsyncLifetime
{
    public Task InitializeAsync() => f.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private static CustomerRegistrationRequest NewCustomer(string email) => new()
    {
        FirstName = "Ana", LastNames = "García Mora", Email = email, Phone = "88112233",
        Password = Password, ConfirmPassword = Password
    };
    private static PetRequest NewPet() => new()
    {
        Name = "Luna", Species = "Perro", Breed = "Mestiza", AgeYears = "2",
        WeightKg = "8.50", HeightCm = "40", Allergies = "Lana"
    };

    [Fact]
    public async Task GID001_RegistrationValidatesRequiredFieldsAndUniqueEmailAcrossRoles()
    {
        using var http = f.Http();
        var missing = await http.PostAsJsonAsync("/api/v1/auth/register", new CustomerRegistrationRequest { FirstName = "Ana" });
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        var missingError = (await missing.Content.ReadFromJsonAsync<ApiError>())!;
        Assert.Equal("Campos obligatorios incompletos", missingError.Message);
        Assert.Contains("LastNames", missingError.Errors!.Keys);
        Assert.Equal(0, await f.Db(db => db.Users.CountAsync(u => u.Email == "")));

        var conflict = await http.PostAsJsonAsync("/api/v1/auth/register", NewCustomer("ADMIN@AMELI.TEST"));
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.Equal("Correo electronico ya utilizado, intenta con otro correo",
            (await conflict.Content.ReadFromJsonAsync<ApiError>())!.Message);

        var created = await http.PostAsJsonAsync("/api/v1/auth/register", NewCustomer("ana@example.test"));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var user = await f.Db(db => db.Users.SingleAsync(u => u.NormalizedEmail == "ANA@EXAMPLE.TEST"));
        Assert.Equal(Roles.Client, user.RoleName); Assert.Equal("Ana", user.FirstName);
        Assert.Equal("García Mora", user.LastNames); Assert.NotEqual(Password, user.PasswordHash);
        await f.Login("ana@example.test", Password);
    }

    [Fact]
    public async Task GID002To005_PetsRemainOwnedValidAndReactivatable()
    {
        var owner = await f.Login(); var other = await f.Login("logistica@ameli.test");
        using var http = f.Http(owner); using var staff = f.Http(other);
        Assert.Empty((await http.GetFromJsonAsync<List<PetView>>("/api/v1/customer/pets"))!);

        var invalid = NewPet(); invalid.WeightKg = "8 lb"; invalid.HeightCm = "-2";
        var rejected = await http.PostAsJsonAsync("/api/v1/customer/pets", invalid);
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        var validation = (await rejected.Content.ReadFromJsonAsync<ApiError>())!;
        Assert.Contains("kg", validation.Message); Assert.Contains("WeightKg", validation.Errors!.Keys);
        Assert.Contains("HeightCm", validation.Errors.Keys);
        Assert.Equal(0, await f.Db(db => db.Pets.CountAsync()));

        var created = await http.PostAsJsonAsync("/api/v1/customer/pets", NewPet());
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var pet = (await created.Content.ReadFromJsonAsync<PetView>())!;
        Assert.Equal(f.Client, await f.Db(db => db.Pets.Where(x => x.Id == pet.Id).Select(x => x.OwnerUserId).SingleAsync()));
        Assert.Single((await http.GetFromJsonAsync<List<PetView>>("/api/v1/customer/pets"))!);
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync($"/api/v1/customer/pets/{pet.Id}")).StatusCode);

        await f.AddUser("otherclient@ameli.test", Roles.Client);
        using var otherClient = f.Http(await f.Login("otherclient@ameli.test"));
        Assert.Empty((await otherClient.GetFromJsonAsync<List<PetView>>("/api/v1/customer/pets"))!);
        Assert.Equal(HttpStatusCode.NotFound, (await otherClient.GetAsync($"/api/v1/customer/pets/{pet.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await otherClient.GetAsync($"/api/v1/customer/pets/{pet.Id}/photo")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await otherClient.PostAsync($"/api/v1/customer/pets/{pet.Id}/deactivate", null)).StatusCode);

        var same = NewPet(); same.Revision = pet.Revision;
        var unchanged = (await (await http.PutAsJsonAsync($"/api/v1/customer/pets/{pet.Id}", same))
            .Content.ReadFromJsonAsync<PetChangeResult>())!;
        Assert.False(unchanged.Changed); Assert.Equal("No existen cambios por aplicar", unchanged.Message);
        Assert.Equal(pet.Revision, unchanged.Pet.Revision);

        var disabled = (await (await http.PostAsync($"/api/v1/customer/pets/{pet.Id}/deactivate", null))
            .Content.ReadFromJsonAsync<PetChangeResult>())!;
        Assert.False(disabled.Pet.IsActive);
        Assert.Single((await http.GetFromJsonAsync<List<PetView>>("/api/v1/customer/pets"))!);
        var enabled = (await (await http.PostAsync($"/api/v1/customer/pets/{pet.Id}/reactivate", null))
            .Content.ReadFromJsonAsync<PetChangeResult>())!;
        Assert.True(enabled.Pet.IsActive); Assert.NotEqual(pet.Revision, enabled.Pet.Revision);
        f.Clock.Advance(TimeSpan.FromMinutes(1));
        var edited = NewPet(); edited.Revision = enabled.Pet.Revision; edited.WeightKg = "9.50"; edited.Allergies = "";
        edited.IsActive = false;
        edited.PhotoFileName = "luna.png";
        const string pixel = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jRZcAAAAASUVORK5CYII=";
        edited.PhotoBase64Data = pixel;
        var updated = (await (await http.PutAsJsonAsync($"/api/v1/customer/pets/{pet.Id}", edited))
            .Content.ReadFromJsonAsync<PetChangeResult>())!;
        Assert.True(updated.Changed); Assert.Equal("", updated.Pet.Allergies);
        Assert.Equal(9.50m, updated.Pet.WeightKg);
        Assert.False(updated.Pet.IsActive); Assert.True(updated.Pet.HasPhoto);
        var image = (await http.GetFromJsonAsync<PetPhotoPayload>($"/api/v1/customer/pets/{pet.Id}/photo"))!;
        Assert.Equal(Convert.FromBase64String(pixel), image.Content);
        Assert.True(updated.Pet.UpdatedAtUtc > pet.UpdatedAtUtc);

        var invalidEdit = NewPet(); invalidEdit.Revision = updated.Pet.Revision;
        invalidEdit.WeightKg = "10"; invalidEdit.PhotoFileName = "wrong.txt";
        invalidEdit.PhotoBase64Data = Convert.ToBase64String("not-an-image"u8.ToArray());
        Assert.Equal(HttpStatusCode.BadRequest,
            (await http.PutAsJsonAsync($"/api/v1/customer/pets/{pet.Id}", invalidEdit)).StatusCode);
        var unchangedAfterError = (await http.GetFromJsonAsync<PetView>($"/api/v1/customer/pets/{pet.Id}"))!;
        Assert.Equal(9.50m, unchangedAfterError.WeightKg);
        Assert.Equal(updated.Pet.Revision, unchangedAfterError.Revision);
    }

    [Fact]
    public async Task GID002And006_ImagesAndAllergicMaterialsAreValidated()
    {
        using var http = f.Http(await f.Login());
        var pet = (await (await http.PostAsJsonAsync("/api/v1/customer/pets", NewPet())).Content.ReadFromJsonAsync<PetView>())!;
        var bad = await http.PostAsJsonAsync($"/api/v1/customer/pets/{pet.Id}/photo", new PetPhotoRequest
        { FileName = "not-image.txt", Base64Data = Convert.ToBase64String("bad"u8.ToArray()) });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        Assert.Equal("invalid_image", (await bad.Content.ReadFromJsonAsync<ApiError>())!.Code);
        var huge = await http.PostAsJsonAsync($"/api/v1/customer/pets/{pet.Id}/photo", new PetPhotoRequest
        { FileName = "large.png", Base64Data = Convert.ToBase64String(new byte[5 * 1024 * 1024 + 1]) });
        Assert.Equal("large_image", (await huge.Content.ReadFromJsonAsync<ApiError>())!.Code);
        const string pixel = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jRZcAAAAASUVORK5CYII=";
        var saved = await http.PostAsJsonAsync($"/api/v1/customer/pets/{pet.Id}/photo", new PetPhotoRequest
        { FileName = "luna.png", Base64Data = pixel });
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var image = (await http.GetFromJsonAsync<PetPhotoPayload>($"/api/v1/customer/pets/{pet.Id}/photo"))!;
        Assert.Equal("image/png", image.ContentType); Assert.Equal(Convert.FromBase64String(pixel), image.Content);

        var allergy = await http.PutAsJsonAsync($"/api/v1/customer/pets/{pet.Id}/preferences",
            new PetPreferencesRequest { Materials = ["Lana"] });
        Assert.Equal(HttpStatusCode.BadRequest, allergy.StatusCode);
        Assert.Equal("allergic_material", (await allergy.Content.ReadFromJsonAsync<ApiError>())!.Code);

        var allowed = await http.PutAsJsonAsync($"/api/v1/customer/pets/{pet.Id}/preferences",
            new PetPreferencesRequest { Materials = ["Algodón"], Colors = ["Azul"] });
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        Assert.Equal("Algodón", (await allowed.Content.ReadFromJsonAsync<PetPreferencesView>())!.Materials.Single());
        var cleared = await http.PutAsJsonAsync($"/api/v1/customer/pets/{pet.Id}/preferences", new PetPreferencesRequest());
        Assert.Empty((await cleared.Content.ReadFromJsonAsync<PetPreferencesView>())!.Materials);
    }

    [Fact]
    public async Task GID007_AddressesHaveRequiredFieldsOwnershipAndDefaultPromotion()
    {
        using var http = f.Http(await f.Login());
        var incomplete = await http.PostAsJsonAsync("/api/v1/customer/addresses", new DeliveryAddressRequest { Province = "San José" });
        Assert.Equal("required_fields", (await incomplete.Content.ReadFromJsonAsync<ApiError>())!.Code);
        Assert.Empty((await http.GetFromJsonAsync<List<DeliveryAddressView>>("/api/v1/customer/addresses"))!);
        DeliveryAddressRequest Address(string street) => new() { Province = "San José", Canton = "San José", District = "Carmen",
            ExactAddress = street, Reference = "Junto al parque" };
        var first = (await (await http.PostAsJsonAsync("/api/v1/customer/addresses", Address("Casa 1")))
            .Content.ReadFromJsonAsync<DeliveryAddressView>())!;
        var second = (await (await http.PostAsJsonAsync("/api/v1/customer/addresses", Address("Casa 2")))
            .Content.ReadFromJsonAsync<DeliveryAddressView>())!;
        Assert.True(first.IsDefault); Assert.False(second.IsDefault);
        await http.PostAsync($"/api/v1/customer/addresses/{second.Id}/default", null);
        Assert.True((await http.GetFromJsonAsync<List<DeliveryAddressView>>("/api/v1/customer/addresses"))!
            .Single(x => x.Id == second.Id).IsDefault);
        await f.AddUser("otherclient@ameli.test", Roles.Client);
        using var other = f.Http(await f.Login("otherclient@ameli.test"));
        Assert.Equal(HttpStatusCode.NotFound, (await other.DeleteAsync($"/api/v1/customer/addresses/{second.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await http.DeleteAsync($"/api/v1/customer/addresses/{second.Id}")).StatusCode);
        Assert.True((await http.GetFromJsonAsync<List<DeliveryAddressView>>("/api/v1/customer/addresses"))!.Single().IsDefault);
    }
}
