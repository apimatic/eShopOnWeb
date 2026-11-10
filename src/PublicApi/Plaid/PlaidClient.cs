using System;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Plaid;

/// <summary>Settings from the <c>Plaid:</c> configuration section.</summary>
public class PlaidSettings
{
    public const string SectionName = "Plaid";

    public string? ClientId { get; set; }
    public string? Secret { get; set; }
    public string? Environment { get; set; }
    /// <summary>Optional. When set it is used verbatim as the API's base address.</summary>
    public string? BaseUrl { get; set; }
}

/// <summary>A failure calling Plaid. Never contains credentials or access tokens.</summary>
public class PlaidException : Exception
{
    public PlaidException(HttpStatusCode httpStatus, string? errorType, string? errorCode, string message)
        : base(message)
    {
        HttpStatus = httpStatus;
        ErrorType = errorType;
        ErrorCode = errorCode;
    }

    public HttpStatusCode HttpStatus { get; }
    public string? ErrorType { get; }
    public string? ErrorCode { get; }

    /// <summary>The status this app answers with when it cannot carry out the request because of this failure.</summary>
    public int ResponseStatus => ErrorCode switch
    {
        "PLAID_NOT_CONFIGURED" => 503,
        "ITEM_LOGIN_REQUIRED" or "PENDING_EXPIRATION" or "ADDITIONAL_CONSENT_REQUIRED" or "PRODUCT_NOT_READY"
            or "ITEM_NOT_SUPPORTED" or "NO_ACCOUNTS" or "TRANSFER_NOT_CANCELLABLE" => 409,
        "RATE_LIMIT" or "TRANSACTIONS_LIMIT" or "INSTITUTION_RATE_LIMIT" => 429,
        _ when ErrorType == "RATE_LIMIT_EXCEEDED" => 429,
        _ when ErrorType is "INVALID_REQUEST" or "INVALID_INPUT" => 400,
        _ when ErrorType == "ITEM_ERROR" => 409,
        _ when ErrorType == "INSTITUTION_ERROR" => 502,
        _ when ErrorType == "INVALID_RESULT" => 409,
        _ when (int)HttpStatus is >= 400 and < 500 && ErrorType == "API_ERROR" => 502,
        _ when (int)HttpStatus is >= 400 and < 500 => 422,
        _ => 502
    };
}

/// <summary>Calls Plaid's HTTP API directly (no SDK).</summary>
public class PlaidClient
{
    private readonly HttpClient _http;
    private readonly PlaidSettings _settings;

    public PlaidClient(HttpClient http, IOptions<PlaidSettings> settings)
    {
        _http = http;
        _settings = settings.Value;
    }

    internal static string? ResolveBaseUrl(PlaidSettings s)
    {
        if (!string.IsNullOrWhiteSpace(s.BaseUrl))
        {
            return s.BaseUrl.Trim();
        }
        return s.Environment?.Trim().ToLowerInvariant() switch
        {
            "sandbox" => "https://sandbox.plaid.com",
            "production" => "https://production.plaid.com",
            "development" => "https://development.plaid.com",
            _ => null
        };
    }

    /// <summary>POSTs a JSON body to a Plaid endpoint (for example <c>/institutions/get</c>) and returns the JSON answer.</summary>
    public async Task<JsonNode> PostAsync(string path, JsonObject body, CancellationToken ct = default, int maxAttempts = 3, int backoffMs = 400)
    {
        if (string.IsNullOrWhiteSpace(_settings.ClientId) || string.IsNullOrWhiteSpace(_settings.Secret))
        {
            throw new PlaidException(HttpStatusCode.ServiceUnavailable, null, "PLAID_NOT_CONFIGURED",
                "Plaid credentials are not configured (Plaid:ClientId / Plaid:Secret).");
        }
        var baseUrl = ResolveBaseUrl(_settings);
        if (baseUrl is null)
        {
            throw new PlaidException(HttpStatusCode.ServiceUnavailable, null, "PLAID_NOT_CONFIGURED",
                "Plaid is not configured: set Plaid:Environment (sandbox, development or production) or Plaid:BaseUrl.");
        }
        var url = baseUrl.TrimEnd('/') + "/" + path.TrimStart('/');
        var payload = body.ToJsonString();

        for (var attempt = 1; ; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            };
            request.Headers.Add("PLAID-CLIENT-ID", _settings.ClientId);
            request.Headers.Add("PLAID-SECRET", _settings.Secret);
            request.Headers.Add("Plaid-Version", "2020-09-14");

            HttpResponseMessage response;
            try
            {
                response = await _http.SendAsync(request, ct);
            }
            catch (HttpRequestException)
            {
                throw new PlaidException(HttpStatusCode.BadGateway, null, "PLAID_UNREACHABLE", "Plaid could not be reached.");
            }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new PlaidException(HttpStatusCode.GatewayTimeout, null, "PLAID_TIMEOUT", "Plaid did not answer in time.");
            }

            using (response)
            {
                var text = await response.Content.ReadAsStringAsync(ct);
                JsonNode? json = null;
                try { json = string.IsNullOrWhiteSpace(text) ? null : JsonNode.Parse(text); } catch (JsonException) { }

                if (response.IsSuccessStatusCode && json is not null)
                {
                    return json;
                }

                var errorCode = json?["error_code"]?.GetValue<string>();
                var errorType = json?["error_type"]?.GetValue<string>();
                var message = json?["display_message"]?.GetValue<string>()
                              ?? json?["error_message"]?.GetValue<string>()
                              ?? $"Plaid answered {(int)response.StatusCode}.";

                // Only rate limiting is retried: money-moving calls must never be repeated blindly.
                if (response.StatusCode == HttpStatusCode.TooManyRequests && attempt < maxAttempts)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(backoffMs * attempt), ct);
                    continue;
                }
                if (response.StatusCode == HttpStatusCode.TooManyRequests && errorCode is null)
                {
                    errorCode = "RATE_LIMIT";
                }
                throw new PlaidException(response.StatusCode, errorType, errorCode, message);
            }
        }
    }

    public static string Money(decimal amount) => amount.ToString("F2", CultureInfo.InvariantCulture);
}
