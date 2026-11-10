using System;
using System.Collections.Generic;
using System.Linq;
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
/// Thin HTTP client for the Mindbody Public API v6. Keeps a staff user token, renewing it before it expires
/// (and once more if Mindbody rejects it), so the process can run indefinitely.
/// </summary>
public sealed class MindbodyClient
{
    public const string HTTP_CLIENT_NAME = "mindbody";
    private static readonly TimeSpan TokenMargin = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan DefaultTokenLifetime = TimeSpan.FromHours(1);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptionsMonitor<MindbodyOptions> _options;
    private readonly ILogger<MindbodyClient> _logger;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private string? _token;
    private DateTimeOffset _tokenExpiresAt;

    public MindbodyClient(IHttpClientFactory httpClientFactory, IOptionsMonitor<MindbodyOptions> options, ILogger<MindbodyClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options;
        _logger = logger;
    }

    public Task<JsonElement> GetAsync(string path, IDictionary<string, string>? query = null, CancellationToken ct = default)
        => SendAsync(HttpMethod.Get, path, query, null, ct);

    public Task<JsonElement> PostAsync(string path, object body, CancellationToken ct = default)
        => SendAsync(HttpMethod.Post, path, null, body, ct);

    /// <summary>Reads every page of a list endpoint.</summary>
    public async Task<List<JsonElement>> GetAllAsync(string path, string arrayName, IDictionary<string, string>? query = null, CancellationToken ct = default)
    {
        var q = query is null ? new Dictionary<string, string>() : new Dictionary<string, string>(query);
        q["Limit"] = "200";
        var items = new List<JsonElement>();
        var offset = 0;
        while (true)
        {
            q["Offset"] = offset.ToString();
            var root = await GetAsync(path, q, ct);
            var page = root.TryGetProperty(arrayName, out var arr) && arr.ValueKind == JsonValueKind.Array
                ? arr.EnumerateArray().ToList()
                : new List<JsonElement>();
            items.AddRange(page);
            offset += page.Count;
            var total = root.TryGetProperty("PaginationResponse", out var p) && p.TryGetProperty("TotalResults", out var t) && t.TryGetInt32(out var tv)
                ? tv
                : items.Count;
            if (page.Count == 0 || offset >= total)
                return items;
        }
    }

    private async Task<JsonElement> SendAsync(HttpMethod method, string path, IDictionary<string, string>? query, object? body, CancellationToken ct)
    {
        var options = _options.CurrentValue;
        if (string.IsNullOrWhiteSpace(options.ApiKey) || string.IsNullOrWhiteSpace(options.SiteId)
            || string.IsNullOrWhiteSpace(options.Username) || string.IsNullOrWhiteSpace(options.Password))
        {
            throw new StudioException(503, "The studio integration is not configured (Mindbody settings are missing).");
        }

        var url = BuildUrl(options, path, query);
        var json = body is null ? null : JsonSerializer.Serialize(body);
        var attempts = method == HttpMethod.Get ? 3 : 1;
        var renewedToken = false;

        for (var attempt = 1; ; attempt++)
        {
            var token = await GetTokenAsync(options, false, ct);
            using var request = new HttpRequestMessage(method, url);
            request.Headers.Add("Api-Key", options.ApiKey);
            request.Headers.Add("SiteId", options.SiteId);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            if (json is not null)
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");

            HttpResponseMessage response;
            string text;
            try
            {
                using var http = _httpClientFactory.CreateClient(HTTP_CLIENT_NAME);
                response = await http.SendAsync(request, ct);
                text = await response.Content.ReadAsStringAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
            {
                if (attempt < attempts)
                {
                    await Task.Delay(200 * attempt, ct);
                    continue;
                }
                _logger.LogWarning(ex, "Mindbody call {Method} {Path} failed", method, path);
                throw new StudioException(503, "The studio schedule service is not reachable right now.", null, ex);
            }

            using (response)
            {
                if (response.IsSuccessStatusCode)
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(text);
                        return doc.RootElement.Clone();
                    }
                    catch (JsonException ex)
                    {
                        throw new StudioException(502, "The studio schedule service returned an unreadable answer.", null, ex);
                    }
                }

                if (response.StatusCode == HttpStatusCode.Unauthorized && !renewedToken)
                {
                    renewedToken = true;
                    await GetTokenAsync(options, true, ct);
                    attempt--;
                    continue;
                }

                var transient = response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500;
                if (transient && attempt < attempts)
                {
                    await Task.Delay(300 * attempt, ct);
                    continue;
                }

                throw ToException(response.StatusCode, text);
            }
        }
    }

    private static string BuildUrl(MindbodyOptions options, string path, IDictionary<string, string>? query)
    {
        var baseUrl = string.IsNullOrWhiteSpace(options.BaseUrl) ? MindbodyOptions.DEFAULT_BASE_URL : options.BaseUrl.Trim();
        var sb = new StringBuilder(baseUrl.TrimEnd('/')).Append('/').Append(path.TrimStart('/'));
        if (query is { Count: > 0 })
        {
            sb.Append('?').Append(string.Join("&", query.Select(kv => Uri.EscapeDataString(kv.Key) + "=" + Uri.EscapeDataString(kv.Value))));
        }
        return sb.ToString();
    }

    private async Task<string> GetTokenAsync(MindbodyOptions options, bool forceRenew, CancellationToken ct)
    {
        if (!forceRenew && _token is not null && DateTimeOffset.UtcNow < _tokenExpiresAt)
            return _token;

        await _tokenLock.WaitAsync(ct);
        try
        {
            if (!forceRenew && _token is not null && DateTimeOffset.UtcNow < _tokenExpiresAt)
                return _token;

            using var request = new HttpRequestMessage(HttpMethod.Post, BuildUrl(options, "usertoken/issue", null));
            request.Headers.Add("Api-Key", options.ApiKey);
            request.Headers.Add("SiteId", options.SiteId);
            request.Content = new StringContent(
                JsonSerializer.Serialize(new { Username = options.Username, Password = options.Password }),
                Encoding.UTF8, "application/json");

            try
            {
                using var http = _httpClientFactory.CreateClient(HTTP_CLIENT_NAME);
                using var response = await http.SendAsync(request, ct);
                var text = await response.Content.ReadAsStringAsync(ct);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError("Mindbody refused the studio credentials (HTTP {Status})", (int)response.StatusCode);
                    throw new StudioException(502, "The studio schedule service did not accept the studio's credentials.");
                }

                using var doc = JsonDocument.Parse(text);
                _token = doc.RootElement.GetProperty("AccessToken").GetString();
                var lifetime = DefaultTokenLifetime;
                if (doc.RootElement.TryGetProperty("Expires", out var exp) && exp.TryGetDateTimeOffset(out var expires))
                    lifetime = expires - DateTimeOffset.UtcNow;
                var usable = lifetime - TokenMargin;
                if (usable < TimeSpan.FromMinutes(1))
                    usable = TimeSpan.FromMinutes(1);
                _tokenExpiresAt = DateTimeOffset.UtcNow + usable;
                return _token!;
            }
            catch (Exception ex) when ((ex is HttpRequestException or OperationCanceledException) && !ct.IsCancellationRequested)
            {
                throw new StudioException(503, "The studio schedule service is not reachable right now.", null, ex);
            }
            catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
            {
                throw new StudioException(502, "The studio schedule service returned an unreadable answer.", null, ex);
            }
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private static StudioException ToException(HttpStatusCode status, string text)
    {
        string? message = null, code = null;
        try
        {
            using var doc = JsonDocument.Parse(text);
            if (doc.RootElement.TryGetProperty("Error", out var err))
            {
                message = err.TryGetProperty("Message", out var m) ? m.GetString() : null;
                code = err.TryGetProperty("Code", out var c) ? c.GetString() : null;
            }
        }
        catch (JsonException)
        {
        }

        message ??= "The studio schedule service answered " + (int)status + ".";
        var mapped = status switch
        {
            HttpStatusCode.NotFound => 404,
            HttpStatusCode.TooManyRequests => 503,
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => 502,
            _ when (int)status >= 500 => 502,
            _ => 422,
        };
        return new StudioException(mapped, message, code);
    }
}
