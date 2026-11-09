using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Payments;

/// <summary>Thin HTTP client over the Stax REST API (no SDK).</summary>
public class StaxClient
{
    private readonly HttpClient _http;
    private readonly StaxOptions _options;

    public StaxClient(HttpClient http, IOptions<StaxOptions> options)
    {
        _http = http;
        _options = options.Value;
    }

    public bool HasApiKey => !string.IsNullOrWhiteSpace(_options.ApiKey);
    public bool HasPlatformKey => !string.IsNullOrWhiteSpace(_options.PlatformApiKey);

    public Task<JsonNode> GetAsync(string path, IDictionary<string, string?>? query = null, CancellationToken ct = default)
        => SendAsync(HttpMethod.Get, path, null, query, false, ct);

    public Task<JsonNode> PostAsync(string path, object? body, CancellationToken ct = default)
        => SendAsync(HttpMethod.Post, path, body ?? new { }, null, false, ct);

    public Task<JsonNode> PutAsync(string path, object? body, CancellationToken ct = default)
        => SendAsync(HttpMethod.Put, path, body ?? new { }, null, false, ct);

    public Task<JsonNode> DeleteAsync(string path, CancellationToken ct = default)
        => SendAsync(HttpMethod.Delete, path, null, null, false, ct);

    public Task<JsonNode> PostAsPlatformAsync(string path, object body, CancellationToken ct = default)
        => SendAsync(HttpMethod.Post, path, body, null, true, ct);

    /// <summary>Reads every page of a paginated Stax list.</summary>
    public async Task<List<JsonNode>> GetAllPagesAsync(string path, IDictionary<string, string?>? query = null, CancellationToken ct = default)
    {
        var all = new List<JsonNode>();
        for (var page = 1; page <= 500; page++)
        {
            var q = new Dictionary<string, string?>(query ?? new Dictionary<string, string?>())
            {
                ["page"] = page.ToString(),
                ["per_page"] = "100"
            };
            var result = await GetAsync(path, q, ct);
            if (result is JsonArray arr)
            {
                foreach (var n in arr) if (n != null) all.Add(n);
                break;
            }
            if (result["data"] is JsonArray data)
                foreach (var n in data) if (n != null) all.Add(n);
            var last = result["last_page"]?.GetValue<int>() ?? 1;
            if (page >= last) break;
        }
        return all;
    }

    private async Task<JsonNode> SendAsync(HttpMethod method, string path, object? body,
        IDictionary<string, string?>? query, bool platform, CancellationToken ct)
    {
        var key = platform ? _options.PlatformApiKey : _options.ApiKey;
        if (string.IsNullOrWhiteSpace(key))
            throw new ApiException(503, platform
                ? "Stax:PlatformApiKey is not configured."
                : "Stax:ApiKey is not configured.");

        var url = _options.EffectiveBaseUrl.TrimEnd('/') + "/" + path.TrimStart('/');
        if (query is { Count: > 0 })
        {
            var sb = new StringBuilder(url).Append('?');
            var first = true;
            foreach (var (k, v) in query)
            {
                if (v == null) continue;
                if (!first) sb.Append('&');
                first = false;
                sb.Append(Uri.EscapeDataString(k)).Append('=').Append(Uri.EscapeDataString(v));
            }
            url = sb.ToString();
        }

        using var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (body != null)
            request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

        HttpResponseMessage response;
        string text;
        try
        {
            response = await _http.SendAsync(request, ct);
            text = await response.Content.ReadAsStringAsync(ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new StaxException(0, "Stax could not be reached: " + ex.Message);
        }

        using (response)
        {
            JsonNode? json = null;
            if (!string.IsNullOrWhiteSpace(text))
            {
                try { json = JsonNode.Parse(text); } catch (JsonException) { }
            }

            if (!response.IsSuccessStatusCode)
                throw new StaxException((int)response.StatusCode, ExtractError(json, text, (int)response.StatusCode));

            return json ?? new JsonObject();
        }
    }

    /// <summary>Flattens the various shapes Stax uses for error bodies into one sentence.</summary>
    public static string ExtractError(JsonNode? json, string raw, int status)
    {
        var parts = new List<string>();
        void Collect(JsonNode? n)
        {
            switch (n)
            {
                case JsonValue v when v.TryGetValue<string>(out var s): parts.Add(s); break;
                case JsonArray a: foreach (var i in a) Collect(i); break;
                case JsonObject o:
                    foreach (var kv in o)
                    {
                        if (kv.Key is "success" or "status" or "code") continue;
                        Collect(kv.Value);
                    }
                    break;
            }
        }
        if (json != null) Collect(json);
        var msg = string.Join(" ", parts.Distinct()).Trim();
        if (msg.Length == 0) msg = string.IsNullOrWhiteSpace(raw) ? "Stax answered " + status + "." : raw;
        return msg.Length > 500 ? msg[..500] : msg;
    }
}
