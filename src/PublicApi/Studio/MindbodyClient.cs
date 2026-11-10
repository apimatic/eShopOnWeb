using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Studio;

/// <summary>
/// Plain-HTTP client for the Mindbody Public API v6 (no SDK). Holds the staff user token and
/// renews it before it expires and whenever Mindbody rejects it, so the process can run for weeks.
/// </summary>
public class MindbodyClient
{
    public const string HttpClientName = "mindbody";
    private const int PageSize = 200;
    private static readonly TimeSpan TokenRenewMargin = TimeSpan.FromMinutes(5);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly MindbodySettings _settings;
    private readonly ILogger<MindbodyClient> _logger;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private string? _token;
    private DateTimeOffset _tokenExpiry;

    public MindbodyClient(IHttpClientFactory httpClientFactory, IOptions<MindbodySettings> settings, ILogger<MindbodyClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _settings = settings.Value;
        _logger = logger;
    }

    private string BaseUrl => string.IsNullOrWhiteSpace(_settings.BaseUrl)
        ? MindbodySettings.DefaultBaseUrl
        : _settings.BaseUrl!.Trim();

    private Uri BuildUri(string path, IEnumerable<KeyValuePair<string, string>>? query)
    {
        var sb = new StringBuilder(BaseUrl.TrimEnd('/')).Append('/').Append(path.TrimStart('/'));
        var first = true;
        if (query != null)
        {
            foreach (var kv in query)
            {
                sb.Append(first ? '?' : '&').Append(Uri.EscapeDataString(kv.Key)).Append('=').Append(Uri.EscapeDataString(kv.Value));
                first = false;
            }
        }
        return new Uri(sb.ToString());
    }

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(_settings.ApiKey) || string.IsNullOrWhiteSpace(_settings.SiteId)
            || string.IsNullOrWhiteSpace(_settings.Username) || string.IsNullOrWhiteSpace(_settings.Password))
        {
            throw new StudioException(503, "The studio integration is not configured.");
        }
    }

    private HttpRequestMessage NewRequest(HttpMethod method, Uri uri)
    {
        var request = new HttpRequestMessage(method, uri);
        request.Headers.TryAddWithoutValidation("Api-Key", _settings.ApiKey);
        request.Headers.TryAddWithoutValidation("SiteId", _settings.SiteId);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return request;
    }

    private async Task<string> GetTokenAsync(string? rejectedToken, CancellationToken ct)
    {
        EnsureConfigured();
        await _tokenLock.WaitAsync(ct);
        try
        {
            var stillValid = _token != null && DateTimeOffset.UtcNow < _tokenExpiry - TokenRenewMargin;
            if (stillValid && (rejectedToken == null || _token != rejectedToken))
            {
                return _token!;
            }

            using var request = NewRequest(HttpMethod.Post, BuildUri("usertoken/issue", null));
            request.Content = new StringContent(
                JsonSerializer.Serialize(new { Username = _settings.Username, Password = _settings.Password }),
                Encoding.UTF8, "application/json");
            Result result;
            try
            {
                result = await SendAsync(request, ct);
            }
            catch (UnauthorizedAccessException)
            {
                throw new StudioException(502, "The studio system refused the integration's credentials.");
            }
            catch (StudioException ex) when (ex.StatusCode is 400 or 404 or 409)
            {
                // The caller did nothing wrong when the integration's own sign-in is refused.
                throw new StudioException(502, "The studio system refused the integration's credentials.");
            }
            var root = result.Body;
            var token = root.Str("AccessToken");
            if (string.IsNullOrEmpty(token))
            {
                throw new StudioException(502, "The studio system did not issue an access token.");
            }

            _token = token;
            var expires = root.Str("Expires");
            _tokenExpiry = expires != null
                && DateTimeOffset.TryParse(expires, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
                ? parsed
                : DateTimeOffset.UtcNow.AddHours(1);
            return token;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private readonly record struct Result(int Status, JsonElement Body);

    /// <summary>Sends one request and returns the parsed JSON body, mapping failures to StudioException.</summary>
    private async Task<Result> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        HttpResponseMessage response;
        string body;
        try
        {
            using var http = _httpClientFactory.CreateClient(HttpClientName);
            response = await http.SendAsync(request, ct);
            body = await response.Content.ReadAsStringAsync(ct);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new StudioException(504, "The studio system did not answer in time.");
        }
        catch (HttpRequestException ex)
        {
            throw new StudioException(502, "The studio system could not be reached.", ex);
        }

        using (response)
        {
            JsonElement root = default;
            var parsedOk = false;
            if (!string.IsNullOrWhiteSpace(body))
            {
                try
                {
                    root = JsonDocument.Parse(body).RootElement.Clone();
                    parsedOk = true;
                }
                catch (JsonException)
                {
                }
            }

            var status = (int)response.StatusCode;
            string? message = null;
            string? code = null;
            var hasError = false;
            if (parsedOk && root.ValueKind == JsonValueKind.Object && root.TryGetProperty("Error", out var err))
            {
                hasError = true;
                message = err.Str("Message");
                code = err.Str("Code");
            }

            if (response.IsSuccessStatusCode && parsedOk && root.ValueKind == JsonValueKind.Object && !hasError)
            {
                return new Result(status, root);
            }

            _logger.LogWarning("Mindbody call failed: {Status} {Code} {Message}", status, code, message);
            if (status == 401)
            {
                throw new UnauthorizedAccessException();
            }
            throw MapError(status, code, message, response.IsSuccessStatusCode);
        }
    }

    private static StudioException MapError(int status, string? code, string? message, bool wasSuccess)
    {
        var reason = string.IsNullOrWhiteSpace(message) ? "The studio system rejected the request." : message!;
        if (status == 429) return new StudioException(429, "The studio system is busy, try again shortly.");
        if (status == 404) return new StudioException(404, reason);
        if (status == 403) return new StudioException(502, "The studio system refused the integration's credentials.");
        if (status >= 500) return new StudioException(502, "The studio system failed to process the request.");
        if (wasSuccess && code == null) return new StudioException(502, "The studio system sent an unexpected answer.");
        var conflict = code is "ValidationFailed" or "InvalidBookingTime" or "ClassFull" or "BookingNotAllowed";
        return new StudioException(conflict ? 409 : 400, reason);
    }

    private async Task<JsonElement> CallAsync(HttpMethod method, string path, IEnumerable<KeyValuePair<string, string>>? query, object? body, CancellationToken ct)
    {
        var uri = BuildUri(path, query);
        var content = body == null ? null : JsonSerializer.Serialize(body);
        var isRead = method == HttpMethod.Get;
        string? rejected = null;
        var transientRetries = isRead ? 2 : 0;
        var tokenRetries = 1;

        while (true)
        {
            var token = await GetTokenAsync(rejected, ct);
            using var request = NewRequest(method, uri);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            if (content != null)
            {
                request.Content = new StringContent(content, Encoding.UTF8, "application/json");
            }

            try
            {
                return (await SendAsync(request, ct)).Body;
            }
            catch (UnauthorizedAccessException)
            {
                if (tokenRetries-- <= 0)
                {
                    throw new StudioException(502, "The studio system refused the integration's credentials.");
                }
                rejected = token;
            }
            catch (StudioException ex) when (ex.StatusCode is 502 or 504 && transientRetries-- > 0)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(300), ct);
            }
        }
    }

    public Task<JsonElement> GetAsync(string path, IEnumerable<KeyValuePair<string, string>>? query, CancellationToken ct)
        => CallAsync(HttpMethod.Get, path, query, null, ct);

    public Task<JsonElement> PostAsync(string path, object body, CancellationToken ct)
        => CallAsync(HttpMethod.Post, path, null, body, ct);

    /// <summary>Reads every page of a list endpoint and returns the items of <paramref name="arrayProperty"/>.</summary>
    public async Task<List<JsonElement>> GetAllAsync(string path, string arrayProperty, IEnumerable<KeyValuePair<string, string>>? query, CancellationToken ct)
    {
        var items = new List<JsonElement>();
        var offset = 0;
        var baseQuery = new List<KeyValuePair<string, string>>(query ?? Array.Empty<KeyValuePair<string, string>>());
        while (true)
        {
            var q = new List<KeyValuePair<string, string>>(baseQuery)
            {
                new("Limit", PageSize.ToString(CultureInfo.InvariantCulture)),
                new("Offset", offset.ToString(CultureInfo.InvariantCulture))
            };
            var root = await GetAsync(path, q, ct);
            var count = 0;
            if (root.TryGetProperty(arrayProperty, out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in arr.EnumerateArray())
                {
                    items.Add(item);
                    count++;
                }
            }

            var total = root.TryGetProperty("PaginationResponse", out var p) ? p.Int("TotalResults") ?? -1 : -1;
            offset += count;
            if (count == 0 || (total >= 0 && offset >= total) || (total < 0 && count < PageSize))
            {
                return items;
            }
        }
    }
}

internal static class JsonExtensions
{
    public static string? Str(this JsonElement e, string name)
        => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v)
            ? v.ValueKind switch
            {
                JsonValueKind.String => v.GetString(),
                JsonValueKind.Number => v.GetRawText(),
                _ => null
            }
            : null;

    public static long? Long(this JsonElement e, string name)
    {
        if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(name, out var v)) return null;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n)) return n;
        if (v.ValueKind == JsonValueKind.String && long.TryParse(v.GetString(), out var s)) return s;
        return null;
    }

    public static int? Int(this JsonElement e, string name) => (int?)e.Long(name);

    public static bool Bool(this JsonElement e, string name, bool fallback = false)
        => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v)
            && (v.ValueKind == JsonValueKind.True || v.ValueKind == JsonValueKind.False)
            ? v.GetBoolean()
            : fallback;

    public static JsonElement Obj(this JsonElement e, string name)
        => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) ? v : default;

    public static IEnumerable<JsonElement> Items(this JsonElement e, string name)
    {
        if (e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array)
        {
            foreach (var i in v.EnumerateArray()) yield return i;
        }
    }

    public static DateTime? LocalDateTime(this JsonElement e, string name)
    {
        var s = e.Str(name);
        return s != null && DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? DateTime.SpecifyKind(d, DateTimeKind.Unspecified)
            : null;
    }
}
