using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Mindbody;

/// <summary>
/// Holds the staff user token for the lifetime of the app and renews it before it expires
/// (the app runs for weeks without a restart).
/// </summary>
public class MindbodyTokenCache
{
    internal readonly SemaphoreSlim Gate = new(1, 1);
    internal string? Token;
    internal DateTimeOffset ExpiresAt = DateTimeOffset.MinValue;
}

/// <summary>
/// Thin HTTP client over the Mindbody Public API v6 (no SDK).
/// </summary>
public class MindbodyClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private readonly MindbodySettings _settings;
    private readonly MindbodyTokenCache _tokens;
    private readonly ILogger<MindbodyClient> _logger;

    public MindbodyClient(HttpClient http, IOptions<MindbodySettings> settings, MindbodyTokenCache tokens, ILogger<MindbodyClient> logger)
    {
        _http = http;
        _settings = settings.Value;
        _tokens = tokens;
        _logger = logger;
    }

    private string BaseUrl
    {
        get
        {
            var url = string.IsNullOrWhiteSpace(_settings.BaseUrl) ? MindbodySettings.DEFAULT_BASE_URL : _settings.BaseUrl!;
            return url.TrimEnd('/');
        }
    }

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(_settings.ApiKey) || string.IsNullOrWhiteSpace(_settings.SiteId)
            || string.IsNullOrWhiteSpace(_settings.Username) || string.IsNullOrWhiteSpace(_settings.Password))
        {
            throw new StudioException(503, "The studio integration is not configured.");
        }
    }

    public Task<T> GetAsync<T>(string path, IEnumerable<KeyValuePair<string, string?>>? query, bool asStaff, CancellationToken ct = default)
        => SendAsync<T>(HttpMethod.Get, path, query, null, asStaff, ct);

    public Task<T> PostAsync<T>(string path, object body, CancellationToken ct = default)
        => SendAsync<T>(HttpMethod.Post, path, null, body, true, ct);

    private static string BuildQuery(IEnumerable<KeyValuePair<string, string?>>? query)
    {
        if (query == null) return string.Empty;
        var joined = string.Join("&", query.Where(q => q.Value != null)
            .Select(q => Uri.EscapeDataString(q.Key) + "=" + Uri.EscapeDataString(q.Value!)));
        return joined.Length == 0 ? string.Empty : "?" + joined;
    }

    private async Task<T> SendAsync<T>(HttpMethod method, string path, IEnumerable<KeyValuePair<string, string?>>? query,
        object? body, bool asStaff, CancellationToken ct)
    {
        EnsureConfigured();
        var url = BaseUrl + path + BuildQuery(query);
        var maxAttempts = method == HttpMethod.Get ? 3 : 1;
        var attempt = 0;
        var refreshedToken = false;

        while (true)
        {
            attempt++;
            var token = asStaff ? await GetTokenAsync(false, ct) : null;
            using var request = new HttpRequestMessage(method, url);
            request.Headers.Add("Api-Key", _settings.ApiKey);
            request.Headers.Add("SiteId", _settings.SiteId);
            if (token != null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            if (body != null) request.Content = JsonContent.Create(body, options: JsonOptions);

            HttpResponseMessage response;
            try
            {
                response = await _http.SendAsync(request, ct);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new StudioException(504, "The studio system did not answer in time.");
            }
            catch (HttpRequestException ex)
            {
                if (attempt < maxAttempts) { await Task.Delay(200 * attempt, ct); continue; }
                throw new StudioException(502, "The studio system is unreachable.", ex);
            }

            using (response)
            {
                var text = await response.Content.ReadAsStringAsync(ct);

                if (response.IsSuccessStatusCode)
                {
                    try
                    {
                        var value = JsonSerializer.Deserialize<T>(text, JsonOptions);
                        if (value != null) return value;
                    }
                    catch (JsonException ex)
                    {
                        throw new StudioException(502, "The studio system returned an unreadable answer.", ex);
                    }
                    throw new StudioException(502, "The studio system returned an empty answer.");
                }

                if (response.StatusCode == HttpStatusCode.Unauthorized && asStaff && !refreshedToken)
                {
                    // The cached token was revoked or expired early: sign in again and retry once.
                    refreshedToken = true;
                    await GetTokenAsync(true, ct);
                    attempt--;
                    continue;
                }

                if ((response.StatusCode == (HttpStatusCode)429 || (int)response.StatusCode >= 500) && attempt < maxAttempts)
                {
                    await Task.Delay(500 * attempt, ct);
                    continue;
                }

                throw ToException(response.StatusCode, text);
            }
        }
    }

    private StudioException ToException(HttpStatusCode status, string text)
    {
        string? message = null;
        try { message = JsonSerializer.Deserialize<MbErrorResponse>(text, JsonOptions)?.Error?.Message; }
        catch (JsonException) { }

        _logger.LogWarning("Mindbody returned {Status}: {Message}", (int)status, message);
        var code = (int)status;
        if (code == 429) return new StudioException(503, "The studio system is busy, please try again.");
        if (code >= 500) return new StudioException(502, "The studio system failed to process the request.");
        if (code is 401 or 403) return new StudioException(502, "The studio system rejected the app's credentials.");
        if (code == 404) return new StudioException(404, message ?? "Not found in the studio system.");
        return new StudioException(409, message ?? "The studio system could not carry out the request.");
    }

    private bool TokenValid => _tokens.Token != null && _tokens.ExpiresAt > DateTimeOffset.UtcNow.AddMinutes(5);

    private async Task<string> GetTokenAsync(bool forceRefresh, CancellationToken ct)
    {
        if (!forceRefresh && TokenValid) return _tokens.Token!;

        await _tokens.Gate.WaitAsync(ct);
        try
        {
            if (!forceRefresh && TokenValid) return _tokens.Token!;

            using var request = new HttpRequestMessage(HttpMethod.Post, BaseUrl + "/usertoken/issue");
            request.Headers.Add("Api-Key", _settings.ApiKey);
            request.Headers.Add("SiteId", _settings.SiteId);
            request.Content = JsonContent.Create(new { Username = _settings.Username, Password = _settings.Password }, options: JsonOptions);

            HttpResponseMessage response;
            try { response = await _http.SendAsync(request, ct); }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new StudioException(504, "The studio system did not answer in time.");
            }
            catch (HttpRequestException ex) { throw new StudioException(502, "The studio system is unreachable.", ex); }

            using (response)
            {
                var text = await response.Content.ReadAsStringAsync(ct);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError("Mindbody token request failed with {Status}", (int)response.StatusCode);
                    throw new StudioException(502, "Could not sign in to the studio system.");
                }
                var issued = JsonSerializer.Deserialize<TokenResponse>(text, JsonOptions);
                if (string.IsNullOrEmpty(issued?.AccessToken))
                    throw new StudioException(502, "Could not sign in to the studio system.");

                _tokens.Token = issued.AccessToken;
                // "Expires" is a UTC instant without offset.
                _tokens.ExpiresAt = issued.Expires is { } e
                    ? new DateTimeOffset(DateTime.SpecifyKind(e, DateTimeKind.Utc))
                    : DateTimeOffset.UtcNow.AddHours(1);
                return _tokens.Token;
            }
        }
        finally
        {
            _tokens.Gate.Release();
        }
    }

    private class TokenResponse
    {
        public string? AccessToken { get; set; }
        public DateTime? Expires { get; set; }
    }
}
