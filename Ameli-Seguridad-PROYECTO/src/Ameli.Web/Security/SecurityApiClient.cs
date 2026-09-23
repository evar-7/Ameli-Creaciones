using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Ameli.Contracts;

namespace Ameli.Web.Security;

public sealed class SecurityApiClient(IHttpClientFactory clients, ApiSessionStore sessions, IHttpContextAccessor accessor)
{
    public const string SessionKeyClaim = "ameli_session";
    public async Task<T> AnonymousAsync<T>(string path, object body, CancellationToken ct = default)
    {
        using var request = Request(HttpMethod.Post, path, body);
        using var response = await clients.CreateClient("Api").SendAsync(request, ct);
        return await ReadAsync<T>(response, ct);
    }
    public async Task<SessionStatus?> ValidateAsync(string key, CancellationToken ct = default)
    {
        try { return await SendForKeyAsync<SessionStatus>(key, HttpMethod.Get, "auth/session", null, ct); }
        catch (ApiFault ex) when (ex.Status == 401) { sessions.Remove(key); return null; }
    }
    public Task<T> SendAsync<T>(HttpMethod method, string path, object? body = null, CancellationToken ct = default)
    {
        var key = accessor.HttpContext?.User.FindFirstValue(SessionKeyClaim);
        if (key is null) throw new ApiFault(401, "invalid_session", "Sesión expirada.");
        return SendForKeyAsync<T>(key, method, path, body, ct);
    }
    public void ForgetCurrent()
    {
        var key = accessor.HttpContext?.User.FindFirstValue(SessionKeyClaim);
        if (key is not null) sessions.Remove(key);
    }
    private async Task<T> SendForKeyAsync<T>(string key, HttpMethod method, string path, object? body, CancellationToken ct)
    {
        var session = sessions.Find(key) ?? throw new ApiFault(401, "invalid_session", "Sesión expirada.");
        await session.Gate.WaitAsync(ct);
        try
        {
            if (session.Tokens.AccessTokenExpiresAtUtc <= DateTimeOffset.UtcNow.AddSeconds(20))
            {
                using var refresh = Request(HttpMethod.Post, "auth/refresh", new RefreshRequest { RefreshToken = session.Tokens.RefreshToken });
                using var refreshResponse = await clients.CreateClient("Api").SendAsync(refresh, ct);
                session.Tokens = await ReadAsync<TokenResponse>(refreshResponse, ct);
            }
            using var request = Request(method, path, body);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.Tokens.AccessToken);
            using var response = await clients.CreateClient("Api").SendAsync(request, ct);
            return await ReadAsync<T>(response, ct);
        }
        catch (ApiFault ex) when (ex.Status == 401) { sessions.Remove(key); throw; }
        finally { session.Gate.Release(); }
    }
    private HttpRequestMessage Request(HttpMethod method, string path, object? body)
    {
        var request = new HttpRequestMessage(method, path);
        if (body is not null) request.Content = JsonContent.Create(body);
        var context = accessor.HttpContext;
        if (context?.Connection.RemoteIpAddress is { } ip) request.Headers.TryAddWithoutValidation("X-Forwarded-For", ip.ToString());
        if (context is not null) request.Headers.TryAddWithoutValidation("User-Agent", context.Request.Headers.UserAgent.ToString());
        return request;
    }
    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken ct)
    {
        if (!response.IsSuccessStatusCode)
        {
            ApiError? error = null;
            try { error = await response.Content.ReadFromJsonAsync<ApiError>(ct); } catch (JsonException) { }
            throw new ApiFault((int)response.StatusCode, error?.Code ?? "invalid_request",
                error?.Message ?? ((int)response.StatusCode == 401 ? "Sesión expirada." : "Revisa los campos e intenta nuevamente."));
        }
        if (response.StatusCode == System.Net.HttpStatusCode.NoContent) return default!;
        return await response.Content.ReadFromJsonAsync<T>(ct) ?? throw new ApiFault(502, "empty_response", "No se recibió una respuesta válida.");
    }
}
