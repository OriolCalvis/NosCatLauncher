using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace NosCatLauncher.Core;

public sealed record AuthSession(string User, string Token, string PlatformGameAccountId);

public sealed class AuthException(string message) : Exception(message);

/// <summary>Talks to NosCore.WebApi's Gameforge-style auth endpoints.</summary>
public sealed class AuthClient(HttpClient http, string baseUrl)
{
    // Gameforge's id for NosTale; NosCore ignores it but the official launcher always sends it.
    private const string PlatformGameId = "dd4e22d6-00d1-44b9-8126-d8b40e0cd7c9";

    private readonly string _baseUrl = baseUrl.TrimEnd('/');

    public async Task<AuthSession> LoginAsync(string user, string password, GameLanguage language, CancellationToken ct = default)
    {
        var body = new
        {
            identity = user,
            password,
            gfLang = language.GfLang,
            locale = language.Locale,
            platformGameId = PlatformGameId,
        };

        using var response = await SendAsync(() => http.PostAsJsonAsync($"{_baseUrl}/api/v1/auth/sessions", body, ct));
        await ThrowIfFailedAsync(response, ct);

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var token = json.RootElement.GetProperty("token").GetString();
        var accountId = json.RootElement.GetProperty("platformGameAccountId").GetString();
        if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(accountId))
        {
            throw new AuthException("The server answered without a token.");
        }

        return new AuthSession(user, token, accountId);
    }

    /// <summary>Each code is single-use: the login server consumes it when the client sends NoS0577.</summary>
    public async Task<string> GetAuthorizationCodeAsync(AuthSession session, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/api/v1/auth/thin/codes")
        {
            Content = JsonContent.Create(new { platformGameAccountId = session.PlatformGameAccountId }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.Token);

        using var response = await SendAsync(() => http.SendAsync(request, ct));
        await ThrowIfFailedAsync(response, ct);

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        return json.RootElement.GetProperty("code").GetString()
               ?? throw new AuthException("The server answered without a code.");
    }

    private async Task<HttpResponseMessage> SendAsync(Func<Task<HttpResponseMessage>> send)
    {
        try
        {
            return await send();
        }
        catch (HttpRequestException ex)
        {
            throw new AuthException($"Cannot reach {_baseUrl}: {ex.Message}");
        }
    }

    private static async Task ThrowIfFailedAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var text = await response.Content.ReadAsStringAsync(ct);
        throw new AuthException(ExtractMessage(text) ?? $"HTTP {(int)response.StatusCode}");
    }

    // NosCore returns localized errors as {"name": "AUTH_INCORRECT", "value": "..."}.
    internal static string? ExtractMessage(string body)
    {
        try
        {
            using var json = JsonDocument.Parse(body);
            if (json.RootElement.ValueKind == JsonValueKind.Object)
            {
                if (json.RootElement.TryGetProperty("value", out var value) && value.ValueKind == JsonValueKind.String)
                {
                    return value.GetString();
                }

                if (json.RootElement.TryGetProperty("title", out var title) && title.ValueKind == JsonValueKind.String)
                {
                    return title.GetString();
                }
            }
        }
        catch (JsonException)
        {
        }

        return string.IsNullOrWhiteSpace(body) ? null : body.Trim();
    }
}
