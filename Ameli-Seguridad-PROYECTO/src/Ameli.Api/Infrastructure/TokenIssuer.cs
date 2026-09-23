using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Ameli.Api.Application;
using Ameli.Api.Domain;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Ameli.Api.Infrastructure;

public sealed class TokenIssuer(IOptions<JwtOptions> jwt, IOptions<SecurityOptions> security, TimeProvider clock)
{
    public (string Token, DateTimeOffset Expires) Issue(AppUser user, AuthSession session)
    {
        var now = clock.GetUtcNow();
        var expires = now.AddMinutes(security.Value.AccessTokenMinutes);
        if (expires > session.AbsoluteExpiresAtUtc) expires = session.AbsoluteExpiresAtUtc;
        var claims = new[]
        {
            new Claim("sub", user.Id.ToString()), new Claim("sid", session.Id.ToString()),
            new Claim("ver", user.SecurityVersion.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new Claim("role", user.RoleName), new Claim("jti", Guid.NewGuid().ToString()),
            new Claim("client_id", "ameli-first-party"),
            new Claim("iat", now.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture), ClaimValueTypes.Integer64)
        };
        var token = new JwtSecurityToken(jwt.Value.Issuer, jwt.Value.Audience, claims,
            now.UtcDateTime, expires.UtcDateTime,
            new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Value.SigningKey)), SecurityAlgorithms.HmacSha256));
        return (new JwtSecurityTokenHandler().WriteToken(token), expires);
    }
    public static string RandomToken() => WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
    public static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
