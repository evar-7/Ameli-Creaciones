using System.Text.Json;
using Ameli.Api.Domain;
using Ameli.Api.Infrastructure;
using Ameli.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Ameli.Api.Application;

public sealed record RecommendationProfile(PetProfile Pet, string[] Colors, string[] Materials,
    string[] GarmentTypes, string[] Styles);

public interface IRecommendationProfileProvider
{
    Task<RecommendationProfile?> GetAsync(Guid ownerUserId, Guid petId, CancellationToken ct);
}

public sealed class DbRecommendationProfileProvider(SecurityDbContext db) : IRecommendationProfileProvider
{
    private static string[] Read(string json) => JsonSerializer.Deserialize<string[]>(json) ?? [];
    public async Task<RecommendationProfile?> GetAsync(Guid ownerUserId, Guid petId, CancellationToken ct)
    {
        var pet = await db.Pets.AsNoTracking().SingleOrDefaultAsync(x => x.Id == petId && x.OwnerUserId == ownerUserId, ct);
        return pet is null ? null : new RecommendationProfile(pet, Read(pet.ColorsJson), Read(pet.MaterialsJson), Read(pet.GarmentTypesJson), Read(pet.StylesJson));
    }
}
