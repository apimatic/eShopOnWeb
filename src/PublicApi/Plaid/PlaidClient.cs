using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Plaid;

/// <summary>Plaid settings, read from the "Plaid" configuration section.</summary>
public class PlaidSettings
{
    public const string SectionName = "Plaid";

    public string? ClientId { get; set; }
    public string? Secret { get; set; }
    public string? Environment { get; set; }
    /// <summary>Optional override. When set it is used verbatim as the API base address.</summary>
    public string? BaseUrl { get; set; }

    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl;
        }

        return (Environment ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "sandbox" => "https://sandbox.plaid.com",
            "development" => "https://development.plaid.com",
            "production" => "https://production.plaid.com",
            _ => throw new ApiException(503, "Plaid is not configured: Plaid:Environment must be sandbox, development or production (or set Plaid:BaseUrl).")
        };
    }
}

/// <summary>An error returned by the Plaid API.</summary>
public class PlaidException : Exception
{
    public PlaidException(int httpStatus, string? errorType, string? errorCode, string message)
        : base(message)
    {
        HttpStatus = httpStatus;
        ErrorType = errorType;
        ErrorCode = errorCode;
    }

    public int HttpStatus { get; }
    public string? ErrorType { get; }
    public string? ErrorCode { get; }

    /// <summary>The HTTP status this app answers with when it can't carry out a request because of this error.</summary>
    public int ApiStatusCode => (ErrorType, ErrorCode, HttpStatus) switch
    {
        (_, "INVALID_API_KEYS" or "INVALID_CLIENT_ID" or "INVALID_SECRET" or "UNAUTHORIZED_ENVIRONMENT" or "UNAUTHORIZED_PRODUCT", _) => 502,
        ("RATE_LIMIT_EXCEEDED", _, _) => 429,
        ("ITEM_ERROR", _, _) => 409,
        ("TRANSFER_ERROR", _, _) => 422,
        ("INVALID_REQUEST" or "INVALID_INPUT", _, _) => 400,
        (_, _, >= 500) => 502,
        _ => 502
    };
}

/// <summary>A minimal HTTP client for the Plaid API (no SDK).</summary>
public class PlaidClient
{
    private readonly HttpClient _http;
    private readonly IOptions<PlaidSettings> _settings;

    public PlaidClient(HttpClient http, IOptions<PlaidSettings> settings)
    {
        _http = http;
        _settings = settings;
    }

    public async Task<JsonObject> PostAsync(string path, JsonObject? body = null, CancellationToken ct = default)
    {
        var settings = _settings.Value;
        if (string.IsNullOrWhiteSpace(settings.ClientId) || string.IsNullOrWhiteSpace(settings.Secret))
        {
            throw new ApiException(503, "Plaid is not configured: Plaid:ClientId and Plaid:Secret are required.");
        }

        var url = settings.ResolveBaseUrl().TrimEnd('/') + "/" + path.TrimStart('/');
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent((body ?? new JsonObject()).ToJsonString(), Encoding.UTF8, "application/json")
        };
        request.Headers.TryAddWithoutValidation("PLAID-CLIENT-ID", settings.ClientId);
        request.Headers.TryAddWithoutValidation("PLAID-SECRET", settings.Secret);
        request.Headers.TryAddWithoutValidation("Plaid-Version", "2020-09-14");

        HttpResponseMessage response;
        string text;
        try
        {
            response = await _http.SendAsync(request, ct);
            text = await response.Content.ReadAsStringAsync(ct);
        }
        catch (HttpRequestException ex)
        {
            throw new ApiException(502, $"Plaid could not be reached: {ex.Message}");
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new ApiException(504, "Plaid did not answer in time.");
        }

        JsonObject? json = null;
        try
        {
            json = JsonNode.Parse(text) as JsonObject;
        }
        catch (JsonException)
        {
        }

        if (!response.IsSuccessStatusCode || json is null)
        {
            var code = json?["error_code"]?.GetValue<string>();
            var type = json?["error_type"]?.GetValue<string>();
            var message = json?["error_message"]?.GetValue<string>()
                          ?? $"Plaid answered with HTTP {(int)response.StatusCode}.";
            throw new PlaidException((int)response.StatusCode, type, code,
                code is null ? message : $"{message} ({code})");
        }

        return json;
    }

    /// <summary>
    /// Posts a request and retries while Plaid is still preparing a product for an item
    /// (for example transactions on an item that was connected without them).
    /// </summary>
    public async Task<JsonObject> PostWhenReadyAsync(string path, Func<JsonObject> body, int attempts = 15, CancellationToken ct = default)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await PostAsync(path, body(), ct);
            }
            catch (PlaidException ex) when (ex.ErrorCode == "PRODUCT_NOT_READY" && attempt < attempts)
            {
                await Task.Delay(TimeSpan.FromSeconds(1.5), ct);
            }
        }
    }
}

/// <summary>An error the API reports to the caller as a 4xx/5xx status with a reason.</summary>
public class ApiException : Exception
{
    public ApiException(int statusCode, string message) : base(message)
    {
        StatusCode = statusCode;
    }

    public int StatusCode { get; }
}
