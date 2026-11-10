using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Banking.Plaid;

/// <summary>
/// The "Plaid:" configuration section.
/// </summary>
public class PlaidOptions
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
            return BaseUrl.Trim();
        }

        var env = string.IsNullOrWhiteSpace(Environment) ? "sandbox" : Environment.Trim().ToLowerInvariant();
        return $"https://{env}.plaid.com";
    }
}

/// <summary>
/// An error answered by Plaid (or a failure to reach it).
/// </summary>
public class PlaidException : Exception
{
    public PlaidException(HttpStatusCode? httpStatus, string? errorType, string? errorCode, string message, string? displayMessage = null)
        : base(message)
    {
        HttpStatus = httpStatus;
        ErrorType = errorType;
        ErrorCode = errorCode;
        DisplayMessage = displayMessage;
    }

    public HttpStatusCode? HttpStatus { get; }
    public string? ErrorType { get; }
    public string? ErrorCode { get; }
    public string? DisplayMessage { get; }
}

/// <summary>
/// Minimal Plaid API client over plain HTTP (no SDK).
/// </summary>
public class PlaidClient
{
    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    private readonly HttpClient _http;
    private readonly PlaidOptions _options;

    public PlaidClient(HttpClient http, IOptions<PlaidOptions> options)
    {
        _http = http;
        _options = options.Value;
    }

    public async Task<T> PostAsync<T>(string path, object? body = null, CancellationToken ct = default, int rateLimitRetries = 2)
    {
        if (string.IsNullOrWhiteSpace(_options.ClientId) || string.IsNullOrWhiteSpace(_options.Secret))
        {
            throw new PlaidException(null, "CONFIGURATION", "NOT_CONFIGURED", "Plaid is not configured (Plaid:ClientId / Plaid:Secret).");
        }

        var url = _options.ResolveBaseUrl().TrimEnd('/') + "/" + path.TrimStart('/');
        var payload = JsonSerializer.Serialize(body ?? new { }, Json);

        for (var attempt = 0; ; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            };
            request.Headers.Add("PLAID-CLIENT-ID", _options.ClientId);
            request.Headers.Add("PLAID-SECRET", _options.Secret);
            request.Headers.Add("Plaid-Version", "2020-09-14");

            HttpResponseMessage response;
            string text;
            try
            {
                response = await _http.SendAsync(request, ct);
                text = await response.Content.ReadAsStringAsync(ct);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
            {
                throw new PlaidException(null, "NETWORK", "UNREACHABLE", "Could not reach Plaid: " + ex.Message);
            }

            using (response)
            {
                if (response.IsSuccessStatusCode)
                {
                    var result = JsonSerializer.Deserialize<T>(text, Json);
                    return result ?? throw new PlaidException(response.StatusCode, "API_ERROR", "EMPTY_RESPONSE", "Plaid returned an empty response.");
                }

                var error = TryParseError(text);
                if (error?.ErrorCode == "RATE_LIMIT_EXCEEDED" && attempt < rateLimitRetries)
                {
                    await Task.Delay(TimeSpan.FromSeconds(1 << attempt), ct);
                    continue;
                }

                throw new PlaidException(
                    response.StatusCode,
                    error?.ErrorType,
                    error?.ErrorCode,
                    error?.ErrorMessage ?? $"Plaid answered HTTP {(int)response.StatusCode}.",
                    error?.DisplayMessage);
            }
        }
    }

    private static PlaidErrorBody? TryParseError(string text)
    {
        try
        {
            return JsonSerializer.Deserialize<PlaidErrorBody>(text, Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private class PlaidErrorBody
    {
        public string? ErrorType { get; set; }
        public string? ErrorCode { get; set; }
        public string? ErrorMessage { get; set; }
        public string? DisplayMessage { get; set; }
    }
}
