using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Studio;

/// <summary>
/// Thin HTTP client for the Mindbody Public API v6. Keeps a staff user token in memory and
/// renews it before it expires (and once on a 401) so the app can run indefinitely.
/// </summary>
public class MindbodyApi
{
    public const string HTTP_CLIENT = "mindbody";
    private const int PAGE_SIZE = 200;
    private static readonly TimeSpan TokenMargin = TimeSpan.FromMinutes(5);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly IHttpClientFactory _factory;
    private readonly IOptionsMonitor<MindbodySettings> _settings;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private string? _token;
    private DateTimeOffset _tokenExpiry;

    public MindbodyApi(IHttpClientFactory factory, IOptionsMonitor<MindbodySettings> settings)
    {
        _factory = factory;
        _settings = settings;
    }

    public Task<T> GetAsync<T>(string path, IEnumerable<KeyValuePair<string, string>>? query, CancellationToken ct)
        => SendAsync<T>(HttpMethod.Get, BuildPath(path, query), null, ct);

    public Task<T> PostAsync<T>(string path, object body, CancellationToken ct)
        => SendAsync<T>(HttpMethod.Post, path, body, ct);

    /// <summary>Reads every page of a list endpoint.</summary>
    public async Task<List<TItem>> GetAllAsync<TPage, TItem>(string path, List<KeyValuePair<string, string>> query,
        Func<TPage, List<TItem>?> items, CancellationToken ct) where TPage : IPaged
    {
        var all = new List<TItem>();
        var offset = 0;
        while (true)
        {
            var q = new List<KeyValuePair<string, string>>(query)
            {
                new("limit", PAGE_SIZE.ToString()),
                new("offset", offset.ToString())
            };
            var page = await GetAsync<TPage>(path, q, ct);
            var got = items(page) ?? new List<TItem>();
            all.AddRange(got);
            offset += got.Count;
            if (got.Count == 0 || page.PaginationResponse is null || offset >= page.PaginationResponse.TotalResults)
                break;
        }
        return all;
    }

    private static string BuildPath(string path, IEnumerable<KeyValuePair<string, string>>? query)
    {
        if (query is null) return path;
        var parts = new List<string>();
        foreach (var kv in query)
            parts.Add(Uri.EscapeDataString(kv.Key) + "=" + Uri.EscapeDataString(kv.Value));
        return parts.Count == 0 ? path : path + "?" + string.Join("&", parts);
    }

    private MindbodySettings RequireSettings()
    {
        var s = _settings.CurrentValue;
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(s.ApiKey)) missing.Add("Mindbody:ApiKey");
        if (string.IsNullOrWhiteSpace(s.SiteId)) missing.Add("Mindbody:SiteId");
        if (string.IsNullOrWhiteSpace(s.Username)) missing.Add("Mindbody:Username");
        if (string.IsNullOrWhiteSpace(s.Password)) missing.Add("Mindbody:Password");
        if (missing.Count > 0)
            throw new StudioException(503, "The studio integration is not configured; missing settings: " + string.Join(", ", missing));
        return s;
    }

    private static Uri BaseAddress(MindbodySettings s)
    {
        var b = string.IsNullOrWhiteSpace(s.BaseUrl) ? MindbodySettings.DEFAULT_BASE_URL : s.BaseUrl!.Trim();
        // A trailing slash is needed so relative paths append to (not replace) the last segment.
        return new Uri(b.EndsWith('/') ? b : b + "/", UriKind.Absolute);
    }

    private async Task<string> GetTokenAsync(MindbodySettings s, bool forceRenew, string? staleToken, CancellationToken ct)
    {
        await _tokenLock.WaitAsync(ct);
        try
        {
            var valid = _token is not null && DateTimeOffset.UtcNow < _tokenExpiry - TokenMargin;
            if (valid && !(forceRenew && _token == staleToken))
                return _token!;

            var http = _factory.CreateClient(HTTP_CLIENT);
            using var req = new HttpRequestMessage(HttpMethod.Post, new Uri(BaseAddress(s), "usertoken/issue"))
            {
                Content = JsonContent.Create(new { s.Username, s.Password })
            };
            AddCommonHeaders(req, s);
            using var resp = await SendRawAsync(http, req, ct);
            var body = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
                throw new StudioException(502, "The studio system rejected the integration credentials.");
            var token = JsonSerializer.Deserialize<TokenResponse>(body, Json);
            if (string.IsNullOrEmpty(token?.AccessToken))
                throw new StudioException(502, "The studio system did not return an access token.");
            _token = token.AccessToken;
            _tokenExpiry = token.Expires ?? DateTimeOffset.UtcNow.AddHours(1);
            return _token;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private static void AddCommonHeaders(HttpRequestMessage req, MindbodySettings s)
    {
        req.Headers.TryAddWithoutValidation("Api-Key", s.ApiKey);
        req.Headers.TryAddWithoutValidation("SiteId", s.SiteId);
    }

    private static async Task<HttpResponseMessage> SendRawAsync(HttpClient http, HttpRequestMessage req, CancellationToken ct)
    {
        try
        {
            return await http.SendAsync(req, ct);
        }
        catch (HttpRequestException)
        {
            throw new StudioException(502, "The studio system could not be reached.");
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new StudioException(504, "The studio system did not answer in time.");
        }
    }

    private async Task<T> SendAsync<T>(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        var s = RequireSettings();
        string? stale = null;
        for (var attempt = 0; ; attempt++)
        {
            var token = await GetTokenAsync(s, attempt > 0, stale, ct);
            var http = _factory.CreateClient(HTTP_CLIENT);
            using var req = new HttpRequestMessage(method, new Uri(BaseAddress(s), path));
            AddCommonHeaders(req, s);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            if (body is not null) req.Content = JsonContent.Create(body, options: Json);

            using var resp = await SendRawAsync(http, req, ct);
            var text = await resp.Content.ReadAsStringAsync(ct);

            if (resp.StatusCode == HttpStatusCode.Unauthorized && attempt == 0)
            {
                stale = token;
                continue;
            }
            if (resp.IsSuccessStatusCode)
            {
                var result = string.IsNullOrWhiteSpace(text) ? default : JsonSerializer.Deserialize<T>(text, Json);
                if (result is null) throw new StudioException(502, "The studio system returned an empty response.");
                return result;
            }
            throw ToException(resp.StatusCode, text);
        }
    }

    private static StudioException ToException(HttpStatusCode status, string text)
    {
        string? message = null;
        try
        {
            message = JsonSerializer.Deserialize<ErrorEnvelope>(text, Json)?.Error?.Message;
        }
        catch (JsonException) { }

        var reason = string.IsNullOrWhiteSpace(message) ? "The studio system could not complete the request." : message;
        if ((int)status == 429) return new StudioException(503, "The studio system is busy, please try again shortly.");
        if ((int)status >= 500) return new StudioException(502, "The studio system failed to process the request.");
        if (status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            return new StudioException(502, "The studio system refused the integration's request.");
        if (status == HttpStatusCode.NotFound) return new StudioException(404, reason);
        return new StudioException(422, reason);
    }

    // ---- wire types ----
    public interface IPaged { PaginationInfo? PaginationResponse { get; } }
    public class PaginationInfo { public int TotalResults { get; set; } }
    private class TokenResponse { public string? AccessToken { get; set; } public DateTimeOffset? Expires { get; set; } }
    private class ErrorEnvelope { public ErrorBody? Error { get; set; } }
    private class ErrorBody { public string? Message { get; set; } }
}
