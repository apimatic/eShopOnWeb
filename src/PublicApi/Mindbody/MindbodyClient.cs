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
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Mindbody;

/// <summary>
/// Thin HTTP client for the Mindbody public API (no SDK). Registered as a singleton so the
/// staff access token is shared and refreshed before it expires.
/// </summary>
public class MindbodyClient
{
    public const string HTTP_CLIENT_NAME = "mindbody";
    private const int PAGE_SIZE = 200;

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly MindbodySettings _settings;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private string? _token;
    private DateTimeOffset _tokenExpiry = DateTimeOffset.MinValue;

    public MindbodyClient(IHttpClientFactory httpClientFactory, IOptions<MindbodySettings> settings)
    {
        _httpClientFactory = httpClientFactory;
        _settings = settings.Value;
    }

    private string BaseUrl => string.IsNullOrWhiteSpace(_settings.BaseUrl) ? MindbodySettings.DEFAULT_BASE_URL : _settings.BaseUrl!;

    private void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(_settings.ApiKey) || string.IsNullOrWhiteSpace(_settings.SiteId)
            || string.IsNullOrWhiteSpace(_settings.Username) || string.IsNullOrWhiteSpace(_settings.Password))
        {
            throw new StudioException(503, "The studio integration is not configured.");
        }
    }

    public Task<JsonElement> GetAsync(string path, IEnumerable<KeyValuePair<string, string>>? query = null, bool authenticated = false, CancellationToken ct = default)
        => SendAsync(HttpMethod.Get, path, query, null, authenticated, ct);

    public Task<JsonElement> PostAsync(string path, object body, CancellationToken ct = default)
        => SendAsync(HttpMethod.Post, path, null, body, true, ct);

    /// <summary>
    /// Reads every page of a list endpoint.
    /// </summary>
    public async Task<List<JsonElement>> GetAllAsync(string path, string arrayProperty, IEnumerable<KeyValuePair<string, string>>? query = null, bool authenticated = false, CancellationToken ct = default)
    {
        var baseQuery = new List<KeyValuePair<string, string>>(query ?? Array.Empty<KeyValuePair<string, string>>());
        var items = new List<JsonElement>();
        var offset = 0;
        while (true)
        {
            var pageQuery = new List<KeyValuePair<string, string>>(baseQuery)
            {
                new("limit", PAGE_SIZE.ToString(CultureInfo.InvariantCulture)),
                new("offset", offset.ToString(CultureInfo.InvariantCulture))
            };
            var page = await GetAsync(path, pageQuery, authenticated, ct);
            var count = 0;
            if (page.TryGetProperty(arrayProperty, out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in arr.EnumerateArray())
                {
                    items.Add(item);
                    count++;
                }
            }

            var total = int.MaxValue;
            if (page.TryGetProperty("PaginationResponse", out var pr) && pr.TryGetProperty("TotalResults", out var t) && t.TryGetInt32(out var ti))
            {
                total = ti;
            }

            offset += count;
            if (count == 0 || offset >= total)
            {
                break;
            }
        }

        return items;
    }

    private async Task<string> GetTokenAsync(bool forceRefresh, string? staleToken, CancellationToken ct)
    {
        await _tokenLock.WaitAsync(ct);
        try
        {
            var stillValid = _token != null && DateTimeOffset.UtcNow < _tokenExpiry - TimeSpan.FromMinutes(5);
            // forceRefresh only applies while the rejected token is still the cached one
            if (stillValid && !(forceRefresh && _token == staleToken))
            {
                return _token!;
            }

            var result = await SendCoreAsync(HttpMethod.Post, "usertoken/issue", null,
                new { Username = _settings.Username, Password = _settings.Password }, null, ct);
            var token = result.GetProperty("AccessToken").GetString()!;
            var expiry = DateTimeOffset.UtcNow.AddHours(1);
            if (result.TryGetProperty("Expires", out var e) && e.ValueKind == JsonValueKind.String
                && DateTimeOffset.TryParse(e.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
            {
                expiry = parsed;
            }

            _token = token;
            _tokenExpiry = expiry;
            return token;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private async Task<JsonElement> SendAsync(HttpMethod method, string path, IEnumerable<KeyValuePair<string, string>>? query, object? body, bool authenticated, CancellationToken ct)
    {
        EnsureConfigured();
        var attempts = method == HttpMethod.Get ? 3 : 1;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                string? token = authenticated ? await GetTokenAsync(false, null, ct) : null;
                try
                {
                    return await SendCoreAsync(method, path, query, body, token, ct);
                }
                catch (MindbodyApiException ex) when (authenticated && ex.HttpStatus == (int)HttpStatusCode.Unauthorized)
                {
                    token = await GetTokenAsync(true, token, ct);
                    return await SendCoreAsync(method, path, query, body, token, ct);
                }
            }
            catch (MindbodyApiException ex) when (attempt < attempts && (ex.HttpStatus == 429 || ex.HttpStatus >= 500))
            {
                await Task.Delay(TimeSpan.FromMilliseconds(300 * attempt), ct);
            }
            catch (HttpRequestException) when (attempt < attempts)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(300 * attempt), ct);
            }
            catch (HttpRequestException ex)
            {
                throw new StudioException(503, "The studio system could not be reached.", ex);
            }
            catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
            {
                if (attempt < attempts)
                {
                    continue;
                }

                throw new StudioException(504, "The studio system did not answer in time.", ex);
            }
        }
    }

    private async Task<JsonElement> SendCoreAsync(HttpMethod method, string path, IEnumerable<KeyValuePair<string, string>>? query, object? body, string? token, CancellationToken ct)
    {
        var url = new StringBuilder(BaseUrl.TrimEnd('/')).Append('/').Append(path.TrimStart('/'));
        if (query != null)
        {
            var first = true;
            foreach (var kv in query)
            {
                url.Append(first ? '?' : '&').Append(Uri.EscapeDataString(kv.Key)).Append('=').Append(Uri.EscapeDataString(kv.Value));
                first = false;
            }
        }

        using var request = new HttpRequestMessage(method, url.ToString());
        request.Headers.TryAddWithoutValidation("Api-Key", _settings.ApiKey);
        request.Headers.TryAddWithoutValidation("SiteId", _settings.SiteId);
        if (token != null)
        {
            request.Headers.TryAddWithoutValidation("Authorization", token);
        }

        if (body != null)
        {
            request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, new MediaTypeHeaderValue("application/json"));
        }

        var http = _httpClientFactory.CreateClient(HTTP_CLIENT_NAME);
        using var response = await http.SendAsync(request, ct);
        var text = await response.Content.ReadAsStringAsync(ct);

        JsonElement root = default;
        var parsedOk = false;
        if (!string.IsNullOrWhiteSpace(text))
        {
            try
            {
                root = JsonDocument.Parse(text).RootElement.Clone();
                parsedOk = true;
            }
            catch (JsonException)
            {
            }
        }

        if (!response.IsSuccessStatusCode)
        {
            string? code = null;
            var message = $"The studio system answered {(int)response.StatusCode}.";
            if (parsedOk && root.ValueKind == JsonValueKind.Object && root.TryGetProperty("Error", out var err) && err.ValueKind == JsonValueKind.Object)
            {
                code = err.TryGetProperty("Code", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() : null;
                if (err.TryGetProperty("Message", out var m) && m.ValueKind == JsonValueKind.String)
                {
                    message = m.GetString()!;
                }
            }

            throw new MindbodyApiException((int)response.StatusCode, code, message);
        }

        if (!parsedOk)
        {
            throw new StudioException(502, "The studio system sent an unreadable answer.");
        }

        return root;
    }
}
