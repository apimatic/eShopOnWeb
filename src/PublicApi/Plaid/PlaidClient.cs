using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;

namespace Microsoft.eShopWeb.PublicApi.Plaid;

/// <summary>A request the app can't carry out; surfaces as a 4xx/5xx with the reason.</summary>
public class AppException : Exception
{
    public int StatusCode { get; }
    public AppException(int statusCode, string message) : base(message) { StatusCode = statusCode; }
}

/// <summary>An error response from the Plaid API.</summary>
public class PlaidApiException : Exception
{
    public int HttpStatus { get; }
    public string ErrorType { get; }
    public string ErrorCode { get; }

    public PlaidApiException(int httpStatus, string errorType, string errorCode, string message) : base(message)
    {
        HttpStatus = httpStatus;
        ErrorType = errorType;
        ErrorCode = errorCode;
    }

    /// <summary>The status code this error is reported to our caller with.</summary>
    public int ToStatusCode() => ErrorCode switch
    {
        "ITEM_LOGIN_REQUIRED" or "PRODUCT_NOT_READY" or "ITEM_NOT_FOUND" or "ADDITIONAL_CONSENT_REQUIRED"
            or "NO_ACCOUNTS" or "ITEM_LOCKED" or "PRODUCTS_NOT_SUPPORTED" or "INVALID_PRODUCT" or "PRODUCT_NOT_ENABLED"
            or "ITEM_NO_VERIFICATION" => 409,
        _ when HttpStatus == 429 || ErrorType == "RATE_LIMIT_EXCEEDED" => 429,
        _ when ErrorType is "INVALID_REQUEST" or "INVALID_INPUT" => 400,
        _ when ErrorType == "TRANSFER_ERROR" => 409,
        _ when ErrorType == "ITEM_ERROR" => 409,
        _ => 502,
    };

    public bool IsProductNotReady => ErrorCode == "PRODUCT_NOT_READY";
}

/// <summary>Thin HTTP client for the Plaid API (no SDK).</summary>
public class PlaidClient
{
    private readonly HttpClient _http;
    private readonly IConfiguration _config;

    public PlaidClient(HttpClient http, IConfiguration config)
    {
        _http = http;
        _config = config;
    }

    public static string? EnvironmentBaseUrl(string? environment) => environment?.Trim().ToLowerInvariant() switch
    {
        "sandbox" => "https://sandbox.plaid.com/",
        "development" => "https://development.plaid.com/",
        "production" => "https://production.plaid.com/",
        _ => null,
    };

    private Uri BuildUri(string path)
    {
        var baseUrl = _config["Plaid:BaseUrl"];
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            baseUrl = EnvironmentBaseUrl(_config["Plaid:Environment"]);
            if (baseUrl == null)
                throw new AppException(503, "Plaid is not configured: set Plaid:Environment (sandbox, development or production) or Plaid:BaseUrl.");
        }
        if (!baseUrl.EndsWith('/')) baseUrl += "/";
        return new Uri(new Uri(baseUrl), path.TrimStart('/'));
    }

    /// <summary>POSTs a JSON body. When <paramref name="retry"/> is set, rate limits and 5xx are retried (for idempotent reads only).</summary>
    public async Task<JsonNode> PostAsync(string path, JsonObject body, bool retry = false, CancellationToken ct = default)
    {
        var clientId = _config["Plaid:ClientId"];
        var secret = _config["Plaid:Secret"];
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(secret))
            throw new AppException(503, "Plaid is not configured: set Plaid:ClientId and Plaid:Secret.");

        var uri = BuildUri(path);
        var attempts = retry ? 3 : 1;
        for (var attempt = 1; ; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, uri)
            {
                Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json")
            };
            request.Headers.Add("PLAID-CLIENT-ID", clientId);
            request.Headers.Add("PLAID-SECRET", secret);

            HttpResponseMessage response;
            try
            {
                response = await _http.SendAsync(request, ct);
            }
            catch (HttpRequestException ex)
            {
                if (attempt < attempts) { await Task.Delay(500 * attempt, ct); continue; }
                throw new AppException(502, "Could not reach Plaid: " + ex.Message);
            }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested)
            {
                if (attempt < attempts) { await Task.Delay(500 * attempt, ct); continue; }
                throw new AppException(504, "Plaid did not respond in time.");
            }

            using (response)
            {
                var text = await response.Content.ReadAsStringAsync(ct);
                if (response.IsSuccessStatusCode)
                {
                    return JsonNode.Parse(text) ?? new JsonObject();
                }

                if (retry && attempt < attempts && ((int)response.StatusCode == 429 || (int)response.StatusCode >= 500))
                {
                    await Task.Delay(700 * attempt, ct);
                    continue;
                }

                string type = "", code = "", message = $"Plaid returned HTTP {(int)response.StatusCode}.";
                try
                {
                    var err = JsonNode.Parse(text);
                    type = err?["error_type"]?.GetValue<string>() ?? "";
                    code = err?["error_code"]?.GetValue<string>() ?? "";
                    message = err?["display_message"]?.GetValue<string>()
                        ?? err?["error_message"]?.GetValue<string>() ?? message;
                }
                catch (JsonException) { }
                if (response.StatusCode == HttpStatusCode.TooManyRequests && type == "") type = "RATE_LIMIT_EXCEEDED";
                throw new PlaidApiException((int)response.StatusCode, type, code, $"Plaid: {message} ({code})".Replace(" ()", ""));
            }
        }
    }

    /// <summary>Like <see cref="PostAsync"/> but waits while Plaid prepares a newly added product (PRODUCT_NOT_READY).</summary>
    public async Task<JsonNode> PostWhenReadyAsync(string path, Func<JsonObject> body, int maxWaitSeconds = 25, CancellationToken ct = default)
    {
        var deadline = DateTime.UtcNow.AddSeconds(maxWaitSeconds);
        while (true)
        {
            try
            {
                return await PostAsync(path, body(), retry: true, ct);
            }
            catch (PlaidApiException ex) when (ex.IsProductNotReady && DateTime.UtcNow < deadline)
            {
                await Task.Delay(2000, ct);
            }
        }
    }
}
