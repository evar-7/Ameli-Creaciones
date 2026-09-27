using Ameli.Contracts;
using Microsoft.Extensions.Caching.Memory;

namespace Ameli.Web.Security;

// The browser receives only an opaque HttpOnly session cookie. JWTs stay on this server.
public sealed class ApiSessionStore(IMemoryCache cache)
{
    public string Add(TokenResponse response)
    {
        var key = Guid.NewGuid().ToString("N");
        cache.Set(key, new StoredSession(response), response.Status.Session.AbsoluteExpiresAtUtc);
        return key;
    }
    public StoredSession? Find(string key) => cache.Get<StoredSession>(key);
    public void Remove(string key) => cache.Remove(key);
}
public sealed class StoredSession(TokenResponse tokens)
{
    public TokenResponse Tokens { get; set; } = tokens;
    public SemaphoreSlim Gate { get; } = new(1, 1);
}
public sealed class ApiFault(int status, string code, string message, ApiError? error = null) : Exception(message)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
    public ApiError Error { get; } = error ?? new(code, message);
}
