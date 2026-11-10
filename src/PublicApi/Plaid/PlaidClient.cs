using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Plaid;

/// <summary>Settings read from the "Plaid:" configuration section.</summary>
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
            return BaseUrl;
        }

        return (Environment ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "sandbox" => "https://sandbox.plaid.com",
            "development" => "https://development.plaid.com",
            "production" => "https://production.plaid.com",
            "" => throw new ApiException(HttpStatusCode.ServiceUnavailable, "Plaid is not configured: set Plaid:Environment (or Plaid:BaseUrl)."),
            var other => throw new ApiException(HttpStatusCode.ServiceUnavailable, $"Unknown Plaid:Environment '{other}'.")
        };
    }
}

/// <summary>A failure the app reports to the caller as an HTTP status with a reason.</summary>
public class ApiException : Exception
{
    public HttpStatusCode StatusCode { get; }

    public ApiException(HttpStatusCode statusCode, string message) : base(message)
    {
        StatusCode = statusCode;
    }
}

/// <summary>An error response returned by Plaid.</summary>
public class PlaidApiException : ApiException
{
    public string ErrorType { get; }
    public string ErrorCode { get; }

    public PlaidApiException(HttpStatusCode status, string errorType, string errorCode, string message)
        : base(Map(status, errorType, errorCode), $"Plaid: {message} ({errorCode})")
    {
        ErrorType = errorType;
        ErrorCode = errorCode;
    }

    private static HttpStatusCode Map(HttpStatusCode status, string type, string code)
    {
        if (code == "RATE_LIMIT_EXCEEDED" || status == HttpStatusCode.TooManyRequests) return HttpStatusCode.TooManyRequests;
        if (type == "ITEM_ERROR" || code is "PRODUCT_NOT_READY" or "ADDITIONAL_CONSENT_REQUIRED" or "PRODUCTS_NOT_SUPPORTED"
            or "NO_ACCOUNTS" or "NO_INVESTMENT_ACCOUNTS" or "NO_AUTH_ACCOUNTS") return HttpStatusCode.Conflict;
        if (type == "TRANSFER_ERROR") return HttpStatusCode.UnprocessableEntity;
        if (type is "INVALID_REQUEST" or "INVALID_INPUT") return HttpStatusCode.BadRequest;
        if (code is "INVALID_PUBLIC_TOKEN" or "INVALID_ACCESS_TOKEN") return HttpStatusCode.BadRequest;
        return HttpStatusCode.BadGateway;
    }
}

/// <summary>Calls the Plaid HTTP API directly, without an SDK.</summary>
public class PlaidClient
{
    private readonly HttpClient _http;
    private readonly IOptionsMonitor<PlaidOptions> _options;

    public PlaidClient(HttpClient http, IOptionsMonitor<PlaidOptions> options)
    {
        _http = http;
        _options = options;
    }

    public async Task<JsonObject> PostAsync(string path, JsonObject body, CancellationToken ct = default)
    {
        var options = _options.CurrentValue;
        if (string.IsNullOrWhiteSpace(options.ClientId) || string.IsNullOrWhiteSpace(options.Secret))
        {
            throw new ApiException(HttpStatusCode.ServiceUnavailable, "Plaid is not configured: set Plaid:ClientId and Plaid:Secret.");
        }

        var url = options.ResolveBaseUrl().TrimEnd('/') + "/" + path.TrimStart('/');
        var payload = body.ToJsonString();

        for (var attempt = 0; ; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            };
            request.Headers.Add("PLAID-CLIENT-ID", options.ClientId);
            request.Headers.Add("PLAID-SECRET", options.Secret);
            request.Headers.Add("Plaid-Version", "2020-09-14");

            HttpResponseMessage response;
            try
            {
                response = await _http.SendAsync(request, ct);
            }
            catch (HttpRequestException ex)
            {
                throw new ApiException(HttpStatusCode.BadGateway, $"Plaid could not be reached: {ex.Message}");
            }

            using (response)
            {
                var text = await response.Content.ReadAsStringAsync(ct);
                if (response.IsSuccessStatusCode)
                {
                    return JsonNode.Parse(text)?.AsObject()
                        ?? throw new ApiException(HttpStatusCode.BadGateway, "Plaid returned an empty response.");
                }

                JsonObject? error = null;
                try { error = JsonNode.Parse(text) as JsonObject; } catch (JsonException) { }

                var type = error?["error_type"]?.GetValue<string>() ?? "API_ERROR";
                var code = error?["error_code"]?.GetValue<string>() ?? response.StatusCode.ToString();
                var message = error?["error_message"]?.GetValue<string>() ?? "Plaid request failed.";

                if ((response.StatusCode == HttpStatusCode.TooManyRequests || code == "RATE_LIMIT_EXCEEDED") && attempt < 3)
                {
                    var delay = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(Math.Pow(2, attempt));
                    await Task.Delay(delay > TimeSpan.FromSeconds(10) ? TimeSpan.FromSeconds(10) : delay, ct);
                    continue;
                }

                throw new PlaidApiException(response.StatusCode, type, code, message);
            }
        }
    }
}
