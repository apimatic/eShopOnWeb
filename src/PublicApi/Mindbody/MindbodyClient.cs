using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Mindbody;

/// <summary>An error reported by Mindbody for a request it understood but refused.</summary>
public class MindbodyApiException : StudioException
{
    public string? Code { get; }
    public int UpstreamStatus { get; }

    public MindbodyApiException(int upstreamStatus, string? code, string message)
        : base(422, message)
    {
        UpstreamStatus = upstreamStatus;
        Code = code;
    }
}

/// <summary>
/// Thin HTTP wrapper over the Mindbody public API v6 (no SDK). Owns the staff user token:
/// it is cached until shortly before it expires, and re-issued when Mindbody rejects it, so the
/// process can run for weeks.
/// </summary>
public class MindbodyClient
{
    public const string HTTP_CLIENT_NAME = "mindbody";
    private const int PAGE_SIZE = 200;
    private const int MAX_PAGES = 500;
    private static readonly TimeSpan TokenSafetyMargin = TimeSpan.FromMinutes(10);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly MindbodyOptions _options;
    private readonly ILogger<MindbodyClient> _logger;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private string? _token;
    private DateTimeOffset _tokenExpiresAt;

    public MindbodyClient(IHttpClientFactory httpClientFactory, IOptions<MindbodyOptions> options, ILogger<MindbodyClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _logger = logger;
    }

    public Task<JsonNode> GetAsync(string path, IEnumerable<KeyValuePair<string, string?>>? query = null, CancellationToken ct = default)
        => SendAsync(HttpMethod.Get, path, query, null, ct);

    public Task<JsonNode> PostAsync(string path, JsonNode body, CancellationToken ct = default)
        => SendAsync(HttpMethod.Post, path, null, body, ct);

    /// <summary>Reads every page of a list endpoint and returns the items of <paramref name="arrayProperty"/>.</summary>
    public async Task<List<JsonNode>> GetAllAsync(string path, string arrayProperty,
        IEnumerable<KeyValuePair<string, string?>>? query = null, CancellationToken ct = default)
    {
        var baseQuery = (query ?? Enumerable.Empty<KeyValuePair<string, string?>>()).ToList();
        var items = new List<JsonNode>();
        var offset = 0;
        for (var page = 0; page < MAX_PAGES; page++)
        {
            var q = new List<KeyValuePair<string, string?>>(baseQuery)
            {
                new("Limit", PAGE_SIZE.ToString(CultureInfo.InvariantCulture)),
                new("Offset", offset.ToString(CultureInfo.InvariantCulture))
            };
            var response = await GetAsync(path, q, ct);
            var pageItems = response[arrayProperty]?.AsArray() ?? new JsonArray();
            foreach (var item in pageItems)
            {
                if (item is not null) items.Add(item);
            }

            var total = response["PaginationResponse"]?["TotalResults"]?.GetValue<int>();
            offset += pageItems.Count;
            if (pageItems.Count == 0 || (total.HasValue && offset >= total.Value) || (!total.HasValue && pageItems.Count < PAGE_SIZE))
            {
                break;
            }
        }
        return items;
    }

    public static string BuildQueryString(IEnumerable<KeyValuePair<string, string?>>? query)
    {
        if (query is null) return string.Empty;
        var sb = new StringBuilder();
        foreach (var (key, value) in query)
        {
            if (value is null) continue;
            sb.Append(sb.Length == 0 ? '?' : '&');
            sb.Append(Uri.EscapeDataString(key)).Append('=').Append(Uri.EscapeDataString(value));
        }
        return sb.ToString();
    }

    private async Task<JsonNode> SendAsync(HttpMethod method, string path,
        IEnumerable<KeyValuePair<string, string?>>? query, JsonNode? body, CancellationToken ct)
    {
        EnsureConfigured();
        var relative = path.TrimStart('/') + BuildQueryString(query);
        var isRead = method == HttpMethod.Get;
        var reauthenticated = false;
        var transientRetries = 0;

        while (true)
        {
            var token = await GetTokenAsync(ct);
            int status;
            string text;
            TimeSpan? retryAfter;
            try
            {
                using var request = new HttpRequestMessage(method, relative);
                request.Headers.TryAddWithoutValidation("Api-Key", _options.ApiKey);
                request.Headers.TryAddWithoutValidation("SiteId", _options.SiteId);
                request.Headers.TryAddWithoutValidation("Authorization", token);
                if (body is not null)
                {
                    request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
                }
                var http = _httpClientFactory.CreateClient(HTTP_CLIENT_NAME);
                using var response = await http.SendAsync(request, ct);
                status = (int)response.StatusCode;
                retryAfter = response.Headers.RetryAfter?.Delta;
                text = await response.Content.ReadAsStringAsync(ct);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                if (isRead && transientRetries++ < 1) continue;
                throw new StudioException(504, "The studio service did not answer in time. Please try again.");
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning(ex, "Mindbody request {Path} failed", path);
                if (isRead && transientRetries++ < 1) continue;
                throw new StudioException(502, "The studio service could not be reached. Please try again.");
            }

            JsonNode? json = null;
            if (!string.IsNullOrWhiteSpace(text))
            {
                try { json = JsonNode.Parse(text); } catch (JsonException) { }
            }

            var errorNode = (json as JsonObject)?["Error"];
            var errorCode = errorNode?["Code"]?.GetValue<string>();
            var errorMessage = errorNode?["Message"]?.GetValue<string>();

            if (status == 401 || IsAuthError(errorCode))
            {
                await InvalidateTokenAsync(token);
                if (!reauthenticated)
                {
                    reauthenticated = true;
                    continue;
                }
                _logger.LogError("Mindbody rejected the staff token twice for {Path}", path);
                throw new StudioException(502, "The studio service rejected the app's credentials.");
            }

            if (status == 429 || status == 503)
            {
                if (isRead && transientRetries++ < 2)
                {
                    var delay = retryAfter ?? TimeSpan.FromSeconds(1 + transientRetries);
                    await Task.Delay(delay > TimeSpan.FromSeconds(10) ? TimeSpan.FromSeconds(10) : delay, ct);
                    continue;
                }
                throw new StudioException(503, "The studio service is busy. Please try again shortly.");
            }

            if (status >= 500)
            {
                _logger.LogWarning("Mindbody {Path} answered {Status}: {Message}", path, status, errorMessage);
                if (isRead && transientRetries++ < 1) continue;
                throw new StudioException(502, "The studio service failed to handle the request.");
            }

            if (errorNode is not null || status >= 400)
            {
                throw new MindbodyApiException(status, errorCode, errorMessage ?? "The studio service refused the request (" + status + ").");
            }

            return json ?? new JsonObject();
        }
    }

    private static bool IsAuthError(string? code)
        => code is not null && (code.Contains("InvalidToken", StringComparison.OrdinalIgnoreCase)
            || code.Contains("TokenExpired", StringComparison.OrdinalIgnoreCase)
            || code.Contains("Unauthorized", StringComparison.OrdinalIgnoreCase));

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey) || string.IsNullOrWhiteSpace(_options.SiteId)
            || string.IsNullOrWhiteSpace(_options.Username) || string.IsNullOrWhiteSpace(_options.Password))
        {
            throw new StudioException(503, "The studio integration is not configured.");
        }
    }

    private async Task InvalidateTokenAsync(string rejected)
    {
        // Only drop the token if it is still the one that was rejected (another request may have refreshed it).
        await _tokenLock.WaitAsync();
        try
        {
            if (_token == rejected) _token = null;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private async Task<string> GetTokenAsync(CancellationToken ct)
    {
        await _tokenLock.WaitAsync(ct);
        try
        {
            if (_token is not null && DateTimeOffset.UtcNow < _tokenExpiresAt) return _token;

            var body = new JsonObject
            {
                ["Username"] = _options.Username,
                ["Password"] = _options.Password
            };
            string text;
            int status;
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, "usertoken/issue");
                request.Headers.TryAddWithoutValidation("Api-Key", _options.ApiKey);
                request.Headers.TryAddWithoutValidation("SiteId", _options.SiteId);
                request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
                var http = _httpClientFactory.CreateClient(HTTP_CLIENT_NAME);
                using var response = await http.SendAsync(request, ct);
                status = (int)response.StatusCode;
                text = await response.Content.ReadAsStringAsync(ct);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new StudioException(504, "The studio service did not answer in time. Please try again.");
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning(ex, "Mindbody token request failed");
                throw new StudioException(502, "The studio service could not be reached. Please try again.");
            }

            JsonNode? json = null;
            try { json = JsonNode.Parse(text); } catch (JsonException) { }
            var accessToken = json?["AccessToken"]?.GetValue<string>();
            if (status != 200 || string.IsNullOrEmpty(accessToken))
            {
                _logger.LogError("Mindbody token issue failed with {Status}: {Message}", status, json?["Error"]?["Message"]);
                throw new StudioException(502, "The studio service rejected the app's credentials.");
            }

            var expires = DateTimeOffset.UtcNow.AddHours(1);
            if (DateTimeOffset.TryParse(json?["Expires"]?.GetValue<string>(), CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed))
            {
                expires = parsed;
            }
            _token = accessToken;
            var lifetime = expires - DateTimeOffset.UtcNow;
            _tokenExpiresAt = lifetime > TokenSafetyMargin * 2
                ? expires - TokenSafetyMargin
                : DateTimeOffset.UtcNow + lifetime / 2;
            return _token;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    /// <summary>Resolves the base address; a configured BaseUrl is used verbatim (a trailing slash is
    /// added only so relative paths resolve beneath it).</summary>
    public static Uri ResolveBaseAddress(MindbodyOptions options)
    {
        var configured = string.IsNullOrWhiteSpace(options.BaseUrl) ? MindbodyOptions.DEFAULT_BASE_URL : options.BaseUrl.Trim();
        if (!configured.EndsWith('/')) configured += "/";
        return new Uri(configured, UriKind.Absolute);
    }
}
