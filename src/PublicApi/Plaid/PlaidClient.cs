using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Plaid;

/// <summary>Bound from the "Plaid" configuration section.</summary>
public class PlaidOptions
{
    public const string SectionName = "Plaid";

    public string? ClientId { get; set; }
    public string? Secret { get; set; }
    public string? Environment { get; set; }
    public string? BaseUrl { get; set; }

    public Uri ResolveBaseAddress()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return new Uri(BaseUrl.Trim().TrimEnd('/') + "/");
        }

        var host = (Environment ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "sandbox" => "https://sandbox.plaid.com/",
            "development" => "https://development.plaid.com/",
            "production" => "https://production.plaid.com/",
            _ => throw new InvalidOperationException(
                "Plaid:Environment must be sandbox, development or production (or set Plaid:BaseUrl).")
        };
        return new Uri(host);
    }
}

/// <summary>An error that should be answered to the caller with a status code and a reason.</summary>
public class ApiException : Exception
{
    public int StatusCode { get; }

    public ApiException(int statusCode, string message) : base(message)
    {
        StatusCode = statusCode;
    }
}

public class PlaidApiException : ApiException
{
    public string ErrorCode { get; }
    public string ErrorType { get; }

    public PlaidApiException(int statusCode, string errorType, string errorCode, string message)
        : base(statusCode, message)
    {
        ErrorType = errorType;
        ErrorCode = errorCode;
    }
}

public interface IPlaidClient
{
    Task<JsonNode> PostAsync(string path, JsonObject body, CancellationToken ct = default);
}

/// <summary>Calls Plaid's HTTP API directly; no SDK.</summary>
public class PlaidClient : IPlaidClient
{
    private readonly HttpClient _http;
    private readonly PlaidOptions _options;

    public PlaidClient(HttpClient http, IOptions<PlaidOptions> options)
    {
        _http = http;
        _options = options.Value;
    }

    public async Task<JsonNode> PostAsync(string path, JsonObject body, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_options.ClientId) || string.IsNullOrWhiteSpace(_options.Secret))
        {
            throw new InvalidOperationException("Plaid:ClientId and Plaid:Secret are not configured.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(_options.ResolveBaseAddress(), path.TrimStart('/')))
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Add("PLAID-CLIENT-ID", _options.ClientId);
        request.Headers.Add("PLAID-SECRET", _options.Secret);
        request.Headers.Add("Plaid-Version", "2020-09-14");

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, ct);
        }
        catch (HttpRequestException)
        {
            throw new ApiException(502, "Plaid could not be reached.");
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new ApiException(504, "Plaid timed out.");
        }

        using (response)
        {
            var text = await response.Content.ReadAsStringAsync(ct);
            JsonNode? node = null;
            try { node = string.IsNullOrWhiteSpace(text) ? null : JsonNode.Parse(text); }
            catch (JsonException) { }

            if (response.IsSuccessStatusCode && node != null)
            {
                return node;
            }

            var errorType = node?["error_type"]?.GetValue<string>() ?? "UNKNOWN";
            var errorCode = node?["error_code"]?.GetValue<string>() ?? "UNKNOWN";
            var message = node?["display_message"]?.GetValue<string>()
                          ?? node?["error_message"]?.GetValue<string>()
                          ?? "Plaid returned an unexpected response.";
            throw new PlaidApiException(MapStatus((int)response.StatusCode, errorCode), errorType, errorCode,
                $"Plaid: {message} ({errorCode})");
        }
    }

    private static int MapStatus(int plaidStatus, string errorCode)
    {
        if (plaidStatus == 429) return 429;
        if (plaidStatus >= 500) return 502;
        if (errorCode == "ITEM_LOGIN_REQUIRED") return 409;
        if (errorCode is "ITEM_NOT_FOUND" or "INVALID_ACCESS_TOKEN") return 404;
        return plaidStatus is >= 400 and < 500 && plaidStatus != 401 && plaidStatus != 403 ? 400 : 502;
    }
}
