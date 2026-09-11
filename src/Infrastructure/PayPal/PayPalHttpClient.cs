using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// Low-level PayPal transport: OAuth2 client-credentials token management (per the spec's
/// security scheme, token URL <c>/v1/oauth2/token</c>), base-URL resolution, request signing
/// and error parsing against PayPal's standard error model. It never logs card data or
/// request bodies.
/// </summary>
public class PayPalHttpClient
{
    private readonly HttpClient _client;
    private readonly PayPalOptions _options;
    private readonly ILogger<PayPalHttpClient> _logger;

    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private string? _accessToken;
    private DateTimeOffset _tokenExpiresAt = DateTimeOffset.MinValue;

    public PayPalHttpClient(IOptions<PayPalOptions> options, ILogger<PayPalHttpClient> logger)
    {
        _options = options.Value;
        _logger = logger;
        _client = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
    }

    public string BaseUrl => _options.ResolveBaseUrl();

    private async Task<string> GetAccessTokenAsync(CancellationToken ct)
    {
        if (_accessToken is not null && DateTimeOffset.UtcNow < _tokenExpiresAt)
            return _accessToken;

        await _tokenLock.WaitAsync(ct);
        try
        {
            if (_accessToken is not null && DateTimeOffset.UtcNow < _tokenExpiresAt)
                return _accessToken;

            using var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/v1/oauth2/token");
            var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.ClientId}:{_options.ClientSecret}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);
            request.Content = new FormUrlEncodedContent(new[]
            {
                new System.Collections.Generic.KeyValuePair<string, string>("grant_type", "client_credentials")
            });

            using var response = await _client.SendAsync(request, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("PayPal token request failed with status {Status}", (int)response.StatusCode);
                throw ParseError(response.StatusCode, body);
            }

            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            _accessToken = root.GetProperty("access_token").GetString();
            var expiresIn = root.TryGetProperty("expires_in", out var exp) ? exp.GetInt32() : 3000;
            // Refresh a minute early to avoid using a token that expires mid-flight.
            _tokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(30, expiresIn - 60));
            return _accessToken!;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    /// <summary>Sends a JSON request to PayPal and returns the parsed response document, or
    /// null for an empty (204) body. Throws <see cref="PayPalApiException"/> on any error.</summary>
    public async Task<JsonDocument?> SendAsync(
        HttpMethod method,
        string path,
        JsonNode? body,
        System.Collections.Generic.IReadOnlyDictionary<string, string>? headers,
        CancellationToken ct)
    {
        var token = await GetAccessTokenAsync(ct);

        using var request = new HttpRequestMessage(method, $"{BaseUrl}{path}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        if (headers is not null)
            foreach (var kvp in headers)
                request.Headers.TryAddWithoutValidation(kvp.Key, kvp.Value);

        if (body is not null)
            request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");

        using var response = await _client.SendAsync(request, ct);
        var responseBody = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            var ex = ParseError(response.StatusCode, responseBody);
            _logger.LogWarning("PayPal {Method} {Path} -> {Status} ({Issue}) debug_id={DebugId}",
                method, path, (int)response.StatusCode, ex.Issue ?? ex.Name, ex.DebugId);
            throw ex;
        }

        _logger.LogInformation("PayPal {Method} {Path} -> {Status}", method, path, (int)response.StatusCode);

        if (string.IsNullOrWhiteSpace(responseBody))
            return null;
        return JsonDocument.Parse(responseBody);
    }

    private static PayPalApiException ParseError(HttpStatusCode status, string body)
    {
        string? name = null, issue = null, description = null, debugId = null;
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.TryGetProperty("name", out var n)) name = n.GetString();
            if (root.TryGetProperty("error", out var e) && name is null) name = e.GetString(); // oauth error shape
            if (root.TryGetProperty("message", out var m)) description = m.GetString();
            if (root.TryGetProperty("error_description", out var ed) && description is null) description = ed.GetString();
            if (root.TryGetProperty("debug_id", out var d)) debugId = d.GetString();
            if (root.TryGetProperty("details", out var details) && details.ValueKind == JsonValueKind.Array && details.GetArrayLength() > 0)
            {
                var first = details[0];
                if (first.TryGetProperty("issue", out var iss)) issue = iss.GetString();
                if (first.TryGetProperty("description", out var dsc)) description = dsc.GetString() ?? description;
            }
        }
        catch (JsonException)
        {
            description = body;
        }

        return new PayPalApiException(status, name, issue, description, debugId, body);
    }
}
