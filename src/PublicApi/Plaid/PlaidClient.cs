using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Plaid;

/// <summary>Settings read from the "Plaid:" configuration section.</summary>
public class PlaidOptions
{
    public const string SectionName = "Plaid";

    public string? ClientId { get; set; }
    public string? Secret { get; set; }
    public string? Environment { get; set; }
    /// <summary>Optional override. When set it is used verbatim as the API's base address.</summary>
    public string? BaseUrl { get; set; }
}

/// <summary>
/// An error the API answers with as a 4xx (or 5xx when the problem is upstream) status and a reason.
/// </summary>
public class ApiProblemException : Exception
{
    public ApiProblemException(int statusCode, string message) : base(message)
    {
        StatusCode = statusCode;
    }

    public int StatusCode { get; }

    public static ApiProblemException BadRequest(string message) => new(400, message);
    public static ApiProblemException NotFound(string message) => new(404, message);
    public static ApiProblemException Conflict(string message) => new(409, message);
    public static ApiProblemException Unprocessable(string message) => new(422, message);
}

/// <summary>An error response from Plaid.</summary>
public class PlaidApiException : ApiProblemException
{
    public PlaidApiException(int statusCode, string errorType, string errorCode, string message)
        : base(statusCode, message)
    {
        ErrorType = errorType;
        ErrorCode = errorCode;
    }

    public string ErrorType { get; }
    public string ErrorCode { get; }
}

/// <summary>
/// Calls Plaid's HTTP API directly (no SDK). Every Plaid endpoint is a JSON POST.
/// </summary>
public class PlaidClient
{
    private const string PlaidVersion = "2020-09-14";
    private static readonly HashSet<string> UpstreamConfigurationErrors = new(StringComparer.OrdinalIgnoreCase)
    {
        "INVALID_API_KEYS", "INVALID_CLIENT_ID", "INVALID_SECRET", "UNAUTHORIZED_ENVIRONMENT", "UNAUTHORIZED_PRODUCT",
        "PRODUCT_NOT_ENABLED"
    };

    private readonly HttpClient _http;
    private readonly PlaidOptions _options;

    public PlaidClient(HttpClient http, IOptions<PlaidOptions> options)
    {
        _http = http;
        _options = options.Value;
    }

    private string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(_options.BaseUrl))
        {
            return _options.BaseUrl;
        }

        return (_options.Environment ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "sandbox" => "https://sandbox.plaid.com",
            "development" => "https://development.plaid.com",
            "production" => "https://production.plaid.com",
            _ => throw new ApiProblemException(503, "Plaid is not configured: set Plaid:Environment to sandbox, development or production, or set Plaid:BaseUrl.")
        };
    }

    /// <summary>POSTs a JSON body to a Plaid endpoint, e.g. <c>/institutions/get</c>, and returns the JSON response.</summary>
    public async Task<JsonNode> PostAsync(string path, object body, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.ClientId) || string.IsNullOrWhiteSpace(_options.Secret))
        {
            throw new ApiProblemException(503, "Plaid is not configured: Plaid:ClientId and Plaid:Secret are required.");
        }

        var url = ResolveBaseUrl().TrimEnd('/') + "/" + path.TrimStart('/');

        // Plaid rate-limits some endpoints; retry a couple of times with a growing delay.
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return await SendAsync(url, body, cancellationToken);
            }
            catch (PlaidApiException ex) when (ex.StatusCode == 429 && attempt < 3)
            {
                await Task.Delay(TimeSpan.FromSeconds(1 + attempt * 2), cancellationToken);
            }
        }
    }

    private async Task<JsonNode> SendAsync(string url, object body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
        };
        request.Headers.Add("PLAID-CLIENT-ID", _options.ClientId);
        request.Headers.Add("PLAID-SECRET", _options.Secret);
        request.Headers.Add("Plaid-Version", PlaidVersion);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        HttpResponseMessage response;
        string content;
        try
        {
            response = await _http.SendAsync(request, cancellationToken);
            content = await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new ApiProblemException(502, $"Could not reach Plaid: {ex.Message}");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ApiProblemException(504, "Plaid did not answer in time.");
        }

        using (response)
        {
            JsonNode? json = null;
            try
            {
                json = string.IsNullOrWhiteSpace(content) ? null : JsonNode.Parse(content);
            }
            catch (JsonException)
            {
                // fall through; handled below
            }

            if (response.IsSuccessStatusCode && json is not null)
            {
                return json;
            }

            var errorType = json?["error_type"]?.GetValue<string>() ?? "UNKNOWN";
            var errorCode = json?["error_code"]?.GetValue<string>() ?? ((int)response.StatusCode).ToString();
            var message = json?["display_message"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(message))
            {
                message = json?["error_message"]?.GetValue<string>() ?? $"Plaid answered with HTTP {(int)response.StatusCode}.";
            }

            throw new PlaidApiException(MapStatus(response.StatusCode, errorType, errorCode), errorType, errorCode, $"Plaid: {message} ({errorCode})");
        }
    }

    private static int MapStatus(HttpStatusCode httpStatus, string errorType, string errorCode)
    {
        if ((int)httpStatus >= 500 || errorType == "API_ERROR" || errorType == "INSTITUTION_ERROR")
        {
            return 502;
        }

        if (httpStatus == HttpStatusCode.TooManyRequests || errorType == "RATE_LIMIT_EXCEEDED")
        {
            return 429;
        }

        if (UpstreamConfigurationErrors.Contains(errorCode) || errorType == "INVALID_RESULT")
        {
            return 502;
        }

        return errorType switch
        {
            "ITEM_ERROR" => 409,
            "TRANSFER_ERROR" or "TRANSFER_REFUND_ERROR" => 422,
            "INVALID_REQUEST" or "INVALID_INPUT" => 400,
            _ => 400
        };
    }
}
