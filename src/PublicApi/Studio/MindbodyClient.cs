using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Studio;

/// <summary>
/// Thin HTTP client for the Mindbody Public API (v6). Registered as a singleton: it caches the
/// staff user token and renews it before it expires (and once more if Mindbody rejects it), so
/// the app keeps working for weeks without a restart.
/// </summary>
public class MindbodyClient
{
    public const string HTTP_CLIENT_NAME = "mindbody";
    private static readonly TimeSpan TokenRenewMargin = TimeSpan.FromMinutes(10);
    private const int MaxTransientAttempts = 3;

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptionsMonitor<MindbodyOptions> _options;
    private readonly ILogger<MindbodyClient> _logger;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private string? _token;
    private DateTimeOffset _tokenExpiresAt = DateTimeOffset.MinValue;

    public MindbodyClient(IHttpClientFactory httpClientFactory, IOptionsMonitor<MindbodyOptions> options, ILogger<MindbodyClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options;
        _logger = logger;
    }

    public Task<JsonNode> GetAsync(string path, IEnumerable<KeyValuePair<string, string>>? query = null, CancellationToken ct = default)
        => SendAsync(HttpMethod.Get, path, query, null, ct);

    public Task<JsonNode> PostAsync(string path, JsonNode body, CancellationToken ct = default)
        => SendAsync(HttpMethod.Post, path, null, body, ct);

    /// <summary>
    /// Reads every page of a list endpoint and returns the items found under <paramref name="arrayName"/>.
    /// </summary>
    public async Task<List<JsonNode>> GetAllPagesAsync(string path, string arrayName,
        IEnumerable<KeyValuePair<string, string>>? query = null, CancellationToken ct = default)
    {
        const int pageSize = 200;
        var baseQuery = new List<KeyValuePair<string, string>>(query ?? Array.Empty<KeyValuePair<string, string>>());
        var items = new List<JsonNode>();
        var offset = 0;
        while (true)
        {
            var q = new List<KeyValuePair<string, string>>(baseQuery)
            {
                new("limit", pageSize.ToString(CultureInfo.InvariantCulture)),
                new("offset", offset.ToString(CultureInfo.InvariantCulture))
            };
            var page = await GetAsync(path, q, ct);
            var array = page[arrayName] as JsonArray;
            var count = 0;
            if (array != null)
            {
                foreach (var item in array)
                {
                    if (item != null)
                    {
                        items.Add(item);
                        count++;
                    }
                }
            }

            var total = page["PaginationResponse"]?["TotalResults"]?.GetValue<int>() ?? items.Count;
            offset += count;
            if (count == 0 || offset >= total)
            {
                return items;
            }
        }
    }

    private string BuildUrl(string path, IEnumerable<KeyValuePair<string, string>>? query)
    {
        var options = _options.CurrentValue;
        var baseUrl = string.IsNullOrWhiteSpace(options.BaseUrl) ? MindbodyOptions.DEFAULT_BASE_URL : options.BaseUrl!;
        var sb = new StringBuilder(baseUrl.TrimEnd('/')).Append('/').Append(path.TrimStart('/'));
        var first = true;
        if (query != null)
        {
            foreach (var (key, value) in query)
            {
                sb.Append(first ? '?' : '&').Append(Uri.EscapeDataString(key)).Append('=').Append(Uri.EscapeDataString(value));
                first = false;
            }
        }

        return sb.ToString();
    }

    private MindbodyOptions RequireOptions()
    {
        var o = _options.CurrentValue;
        if (string.IsNullOrWhiteSpace(o.ApiKey) || string.IsNullOrWhiteSpace(o.SiteId) ||
            string.IsNullOrWhiteSpace(o.Username) || string.IsNullOrWhiteSpace(o.Password))
        {
            throw new StudioException(503, "The studio integration is not configured.");
        }

        return o;
    }

    private async Task<string> GetTokenAsync(bool forceRefresh, string? staleToken, CancellationToken ct)
    {
        if (!forceRefresh && _token != null && DateTimeOffset.UtcNow < _tokenExpiresAt - TokenRenewMargin)
        {
            return _token;
        }

        await _tokenLock.WaitAsync(ct);
        try
        {
            // Someone else may have renewed it while we waited.
            if (_token != null && DateTimeOffset.UtcNow < _tokenExpiresAt - TokenRenewMargin &&
                (!forceRefresh || _token != staleToken))
            {
                return _token;
            }

            var o = RequireOptions();
            var body = new JsonObject { ["Username"] = o.Username, ["Password"] = o.Password };
            JsonNode result;
            try
            {
                result = await SendCoreAsync(HttpMethod.Post, "usertoken/issue", null, body, null, ct);
            }
            catch (TransientException)
            {
                throw new StudioException(503, "The studio system is temporarily unavailable. Please try again.");
            }
            catch (MindbodyException)
            {
                throw new StudioException(502, "The studio system rejected our credentials.");
            }

            var token = result["AccessToken"]?.GetValue<string>();
            if (string.IsNullOrEmpty(token))
            {
                throw new StudioException(502, "The studio system did not issue an access token.");
            }

            var expires = DateTimeOffset.UtcNow.AddHours(1);
            var expiresText = result["Expires"]?.GetValue<string>();
            if (expiresText != null &&
                DateTimeOffset.TryParse(expiresText, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
            {
                expires = parsed;
            }

            _token = token;
            _tokenExpiresAt = expires;
            return token;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private async Task<JsonNode> SendAsync(HttpMethod method, string path,
        IEnumerable<KeyValuePair<string, string>>? query, JsonNode? body, CancellationToken ct)
    {
        var token = await GetTokenAsync(false, null, ct);
        try
        {
            return await SendWithRetryAsync(method, path, query, body, token, ct);
        }
        catch (UnauthorizedTokenException)
        {
            token = await GetTokenAsync(true, token, ct);
            try
            {
                return await SendWithRetryAsync(method, path, query, body, token, ct);
            }
            catch (UnauthorizedTokenException)
            {
                throw new StudioException(502, "The studio system rejected our credentials.");
            }
        }
    }

    private async Task<JsonNode> SendWithRetryAsync(HttpMethod method, string path,
        IEnumerable<KeyValuePair<string, string>>? query, JsonNode? body, string token, CancellationToken ct)
    {
        // Only reads are repeated automatically; a write that may have reached Mindbody is not.
        var attempts = method == HttpMethod.Get ? MaxTransientAttempts : 1;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await SendCoreAsync(method, path, query, body, token, ct);
            }
            catch (TransientException ex) when (attempt < attempts)
            {
                _logger.LogWarning("Mindbody {Path} failed transiently ({Reason}); attempt {Attempt}", path, ex.Message, attempt);
                await Task.Delay(TimeSpan.FromMilliseconds(250 * attempt), ct);
            }
            catch (TransientException)
            {
                throw new StudioException(503, "The studio system is temporarily unavailable. Please try again.");
            }
        }
    }

    private async Task<JsonNode> SendCoreAsync(HttpMethod method, string path,
        IEnumerable<KeyValuePair<string, string>>? query, JsonNode? body, string? token, CancellationToken ct)
    {
        var o = RequireOptions();
        using var request = new HttpRequestMessage(method, BuildUrl(path, query));
        request.Headers.TryAddWithoutValidation("Api-Key", o.ApiKey);
        request.Headers.TryAddWithoutValidation("SiteId", o.SiteId);
        if (token != null)
        {
            request.Headers.TryAddWithoutValidation("Authorization", token);
        }

        if (body != null)
        {
            request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        }

        HttpResponseMessage response;
        string text;
        try
        {
            var client = _httpClientFactory.CreateClient(HTTP_CLIENT_NAME);
            response = await client.SendAsync(request, ct);
            text = await response.Content.ReadAsStringAsync(ct);
        }
        catch (HttpRequestException ex)
        {
            throw new TransientException(ex.Message);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new TransientException("timeout: " + ex.Message);
        }

        using (response)
        {
            JsonNode? json = null;
            if (!string.IsNullOrWhiteSpace(text))
            {
                try { json = JsonNode.Parse(text); } catch (System.Text.Json.JsonException) { }
            }

            var error = json?["Error"];
            if (response.IsSuccessStatusCode && error == null)
            {
                return json ?? new JsonObject();
            }

            var code = error?["Code"]?.GetValue<string>();
            var message = error?["Message"]?.GetValue<string>() ?? $"Mindbody answered {(int)response.StatusCode}.";
            var status = (int)response.StatusCode;

            if (status == (int)HttpStatusCode.Unauthorized)
            {
                if (token == null)
                {
                    throw new MindbodyException(502, code, message);
                }

                throw new UnauthorizedTokenException();
            }

            if (status == (int)HttpStatusCode.TooManyRequests || status >= 500)
            {
                throw new TransientException($"HTTP {status}");
            }

            if (status == (int)HttpStatusCode.Forbidden)
            {
                _logger.LogError("Mindbody refused {Path}: {Message}", path, message);
                throw new StudioException(502, "The studio system refused the request.");
            }

            // A request Mindbody could not carry out; surface its reason as a client error.
            var mapped = code == "InvalidBookingTime" ? 409 : 400;
            throw new MindbodyException(mapped, code, message);
        }
    }

    private sealed class TransientException : Exception
    {
        public TransientException(string message) : base(message) { }
    }

    private sealed class UnauthorizedTokenException : Exception
    {
    }
}
