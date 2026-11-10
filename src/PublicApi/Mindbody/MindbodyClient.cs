using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Mindbody;

/// <summary>
/// Thin HTTP client for the Mindbody Public API v6. Keeps a staff user token, renewing it
/// before it expires (and once on a 401) so the app can run for weeks without a restart.
/// </summary>
public class MindbodyClient
{
    public const string HTTP_CLIENT_NAME = "mindbody";
    private const int PAGE_SIZE = 200;
    private const int MAX_GET_ATTEMPTS = 3;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptionsMonitor<MindbodyOptions> _options;
    private readonly ILogger<MindbodyClient> _logger;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private string? _token;
    private DateTimeOffset _tokenExpiry = DateTimeOffset.MinValue;

    public MindbodyClient(IHttpClientFactory httpClientFactory, IOptionsMonitor<MindbodyOptions> options, ILogger<MindbodyClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options;
        _logger = logger;
    }

    public static string FormatQuery(params (string Key, object? Value)[] parameters)
    {
        var sb = new StringBuilder();
        foreach (var (key, value) in parameters)
        {
            if (value is null) continue;
            var text = value switch
            {
                DateTime dt => dt.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture),
                bool b => b ? "true" : "false",
                IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
                _ => value.ToString()
            };
            sb.Append(sb.Length == 0 ? '?' : '&').Append(Uri.EscapeDataString(key)).Append('=').Append(Uri.EscapeDataString(text ?? ""));
        }
        return sb.ToString();
    }

    public async Task<List<T>> GetAllAsync<T>(string path, string listProperty, string query, CancellationToken ct = default)
    {
        var results = new List<T>();
        var offset = 0;
        while (true)
        {
            var separator = query.Length == 0 ? "?" : "&";
            using var doc = await SendAsync(HttpMethod.Get, $"{path}{query}{separator}Limit={PAGE_SIZE}&Offset={offset}", null, ct);
            var root = doc.RootElement;
            var count = 0;
            if (root.TryGetProperty(listProperty, out var list) && list.ValueKind == JsonValueKind.Array)
            {
                foreach (var element in list.EnumerateArray())
                {
                    var item = element.Deserialize<T>(JsonOptions);
                    if (item is not null) results.Add(item);
                    count++;
                }
            }

            var total = 0;
            if (root.TryGetProperty("PaginationResponse", out var pagination)
                && pagination.TryGetProperty("TotalResults", out var totalElement)
                && totalElement.TryGetInt32(out var parsedTotal))
            {
                total = parsedTotal;
            }

            offset += count;
            if (count == 0 || offset >= total) break;
        }
        return results;
    }

    public async Task<JsonDocument> GetJsonAsync(string path, string query, CancellationToken ct = default)
        => await SendAsync(HttpMethod.Get, path + query, null, ct);

    public async Task<JsonDocument> PostAsync(string path, object body, CancellationToken ct = default)
        => await SendAsync(HttpMethod.Post, path, JsonSerializer.Serialize(body), ct);

    public static T? Read<T>(JsonElement element) => element.Deserialize<T>(JsonOptions);

    private async Task<JsonDocument> SendAsync(HttpMethod method, string pathAndQuery, string? jsonBody, CancellationToken ct)
    {
        var options = _options.CurrentValue;
        EnsureConfigured(options);
        var url = options.EffectiveBaseUrl.TrimEnd('/') + "/" + pathAndQuery.TrimStart('/');
        var attempts = method == HttpMethod.Get ? MAX_GET_ATTEMPTS : 1;
        var refreshedToken = false;

        for (var attempt = 1; ; attempt++)
        {
            var token = await GetTokenAsync(options, forceRefresh: false, ct);
            HttpResponseMessage response;
            string content;
            try
            {
                using var request = new HttpRequestMessage(method, url);
                request.Headers.TryAddWithoutValidation("Api-Key", options.ApiKey);
                request.Headers.TryAddWithoutValidation("SiteId", options.SiteId);
                request.Headers.TryAddWithoutValidation("Authorization", token);
                if (jsonBody is not null)
                {
                    request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
                }
                var http = _httpClientFactory.CreateClient(HTTP_CLIENT_NAME);
                response = await http.SendAsync(request, ct);
                content = await response.Content.ReadAsStringAsync(ct);
            }
            catch (Exception ex) when ((ex is HttpRequestException || ex is TaskCanceledException || ex is TimeoutException) && !ct.IsCancellationRequested)
            {
                if (attempt < attempts)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(300 * attempt), ct);
                    continue;
                }
                _logger.LogWarning(ex, "Mindbody request {Method} {Path} failed", method, pathAndQuery);
                throw new StudioException(HttpStatusCode.GatewayTimeout, "The studio booking service did not respond in time. Please try again.");
            }

            if (response.StatusCode == HttpStatusCode.Unauthorized && !refreshedToken)
            {
                refreshedToken = true;
                await GetTokenAsync(options, forceRefresh: true, ct);
                attempt--;
                continue;
            }

            if (response.IsSuccessStatusCode)
            {
                return JsonDocument.Parse(string.IsNullOrWhiteSpace(content) ? "{}" : content);
            }

            var status = (int)response.StatusCode;
            if (attempt < attempts && (status >= 500 || status == 429))
            {
                await Task.Delay(TimeSpan.FromMilliseconds(400 * attempt), ct);
                continue;
            }

            throw MapError(status, content, pathAndQuery);
        }
    }

    private StudioException MapError(int status, string content, string path)
    {
        string? message = null;
        string? code = null;
        try
        {
            using var doc = JsonDocument.Parse(content);
            if (doc.RootElement.TryGetProperty("Error", out var error))
            {
                var parsed = Read<MbError>(error);
                message = parsed?.Message;
                code = parsed?.Code;
            }
        }
        catch (JsonException)
        {
        }

        _logger.LogWarning("Mindbody returned {Status} for {Path}: {Code} {Message}", status, path, code, message);

        if (status == 429)
            return new StudioException(HttpStatusCode.ServiceUnavailable, "The studio booking service is busy. Please try again shortly.");
        if (status >= 500 || status == 401 || status == 403)
            return new StudioException(HttpStatusCode.BadGateway, "The studio booking service is unavailable right now.");

        var reason = string.IsNullOrWhiteSpace(message) ? "The studio rejected the request." : message;
        var mapped = code switch
        {
            "InvalidClassId" or "InvalidClientId" => HttpStatusCode.NotFound,
            "MissingRequiredFields" or "InvalidParameter" => HttpStatusCode.BadRequest,
            _ => HttpStatusCode.Conflict
        };
        if (status == 404) mapped = HttpStatusCode.NotFound;
        return new StudioException(mapped, reason);
    }

    private static void EnsureConfigured(MindbodyOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.ApiKey) || string.IsNullOrWhiteSpace(options.SiteId)
            || string.IsNullOrWhiteSpace(options.Username) || string.IsNullOrWhiteSpace(options.Password))
        {
            throw new StudioException(HttpStatusCode.ServiceUnavailable, "The studio integration is not configured.");
        }
    }

    private bool TokenIsFresh() => _token is not null && DateTimeOffset.UtcNow < _tokenExpiry - TimeSpan.FromMinutes(5);

    private async Task<string> GetTokenAsync(MindbodyOptions options, bool forceRefresh, CancellationToken ct)
    {
        if (!forceRefresh && TokenIsFresh()) return _token!;

        await _tokenLock.WaitAsync(ct);
        try
        {
            if (!forceRefresh && TokenIsFresh()) return _token!;

            var url = options.EffectiveBaseUrl.TrimEnd('/') + "/usertoken/issue";
            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.TryAddWithoutValidation("Api-Key", options.ApiKey);
            request.Headers.TryAddWithoutValidation("SiteId", options.SiteId);
            request.Content = new StringContent(
                JsonSerializer.Serialize(new { Username = options.Username, Password = options.Password }),
                Encoding.UTF8, "application/json");

            HttpResponseMessage response;
            string content;
            try
            {
                var http = _httpClientFactory.CreateClient(HTTP_CLIENT_NAME);
                response = await http.SendAsync(request, ct);
                content = await response.Content.ReadAsStringAsync(ct);
            }
            catch (Exception ex) when ((ex is HttpRequestException || ex is TaskCanceledException) && !ct.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "Mindbody token request failed");
                throw new StudioException(HttpStatusCode.GatewayTimeout, "The studio booking service did not respond in time. Please try again.");
            }

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Mindbody token request returned {Status}", (int)response.StatusCode);
                throw new StudioException(HttpStatusCode.BadGateway, "The studio booking service is unavailable right now.");
            }

            using var doc = JsonDocument.Parse(content);
            var accessToken = doc.RootElement.GetProperty("AccessToken").GetString();
            if (string.IsNullOrEmpty(accessToken))
                throw new StudioException(HttpStatusCode.BadGateway, "The studio booking service is unavailable right now.");

            var expiry = DateTimeOffset.UtcNow.AddHours(23);
            if (doc.RootElement.TryGetProperty("Expires", out var expires)
                && expires.ValueKind == JsonValueKind.String
                && DateTimeOffset.TryParse(expires.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
            {
                expiry = parsed;
            }

            _token = accessToken;
            _tokenExpiry = expiry;
            return accessToken;
        }
        finally
        {
            _tokenLock.Release();
        }
    }
}
