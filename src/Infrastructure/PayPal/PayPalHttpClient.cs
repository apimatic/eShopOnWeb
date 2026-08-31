using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// Low-level PayPal REST client built against the OpenAPI specifications in
/// api-specs/paypal. Handles OAuth2 client-credentials authentication (token URL
/// /v1/oauth2/token per the specs' security scheme), the PayPal-Request-Id
/// idempotency header and the specs' error model. Never logs request bodies, so full
/// card details cannot end up in logs.
/// </summary>
public class PayPalHttpClient
{
    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly PayPalSettings _settings;
    private readonly ILogger<PayPalHttpClient> _logger;
    private readonly SemaphoreSlim _tokenLock = new SemaphoreSlim(1, 1);

    private string? _accessToken;
    private DateTimeOffset _tokenExpiresAt = DateTimeOffset.MinValue;

    public PayPalHttpClient(HttpClient httpClient, IOptions<PayPalSettings> settings, ILogger<PayPalHttpClient> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _logger = logger;

        var baseUrl = _settings.ResolveBaseUrl();
        _httpClient.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
    }

    public async Task<T?> SendAsync<T>(HttpMethod method, string path, object? body = null, string? requestId = null, CancellationToken cancellationToken = default)
    {
        var token = await GetAccessTokenAsync(cancellationToken);

        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (!string.IsNullOrEmpty(requestId))
        {
            request.Headers.TryAddWithoutValidation("PayPal-Request-Id", requestId);
        }
        // Ask PayPal for the full resource representation (spec: Prefer header).
        request.Headers.TryAddWithoutValidation("Prefer", "return=representation");

        if (body != null)
        {
            request.Content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");
        }
        else if (method == HttpMethod.Post)
        {
            request.Content = new StringContent("{}", Encoding.UTF8, "application/json");
        }

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var content = response.Content == null ? string.Empty : await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw ToException(response.StatusCode, content);
        }

        if (string.IsNullOrWhiteSpace(content))
        {
            return default;
        }

        return JsonSerializer.Deserialize<T>(content, JsonOptions);
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (_accessToken != null && DateTimeOffset.UtcNow < _tokenExpiresAt)
        {
            return _accessToken;
        }

        await _tokenLock.WaitAsync(cancellationToken);
        try
        {
            if (_accessToken != null && DateTimeOffset.UtcNow < _tokenExpiresAt)
            {
                return _accessToken;
            }

            if (string.IsNullOrEmpty(_settings.ClientId) || string.IsNullOrEmpty(_settings.ClientSecret))
            {
                throw new InvalidOperationException(
                    "PayPal credentials are not configured. Set the PayPal:ClientId and PayPal:ClientSecret configuration keys " +
                    "(e.g. via .NET user-secrets, populated from the PAYPAL_CLIENT_ID / PAYPAL_CLIENT_SECRET environment variables).");
            }

            // OAuth2 client credentials grant; token URL /v1/oauth2/token per the specs'
            // security scheme. The base address honors the PayPal:BaseUrl override.
            using var request = new HttpRequestMessage(HttpMethod.Post, "v1/oauth2/token");
            var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_settings.ClientId}:{_settings.ClientSecret}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials"
            });

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw ToException(response.StatusCode, content);
            }

            var token = JsonSerializer.Deserialize<TokenResponseDto>(content, JsonOptions)
                ?? throw new PayPalApiException(response.StatusCode, null, null, "PayPal returned an empty OAuth token response.");

            _accessToken = token.AccessToken ?? throw new PayPalApiException(response.StatusCode, null, null, "PayPal returned an OAuth token response without an access_token.");
            // Refresh a minute early to avoid using a token at the edge of expiry.
            _tokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(token.ExpiresIn - 60, 30));
            return _accessToken;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private PayPalApiException ToException(HttpStatusCode statusCode, string content)
    {
        string? name = null;
        string? debugId = null;
        var message = $"PayPal API request failed with status {(int)statusCode}.";

        try
        {
            var error = JsonSerializer.Deserialize<ErrorResponseDto>(content, JsonOptions);
            if (error != null)
            {
                name = error.Name;
                debugId = error.DebugId;
                if (!string.IsNullOrEmpty(error.Message))
                {
                    message = $"PayPal error {error.Name}: {error.Message}";
                }
                if (error.Details != null)
                {
                    foreach (var detail in error.Details)
                    {
                        if (!string.IsNullOrEmpty(detail.Issue))
                        {
                            message += $" [{detail.Issue}{(string.IsNullOrEmpty(detail.Description) ? "" : $": {detail.Description}")}]";
                        }
                    }
                }
            }
        }
        catch (JsonException)
        {
            // Non-JSON error body; keep the generic message.
        }

        // debug_id is PayPal's correlation id and contains no card data; safe to log.
        _logger.LogWarning("PayPal API error {StatusCode} {Name} (debug_id {DebugId})", (int)statusCode, name, debugId);
        return new PayPalApiException(statusCode, name, debugId, message);
    }
}
