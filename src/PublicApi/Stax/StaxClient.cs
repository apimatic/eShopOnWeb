using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Stax;

/// <summary>Thin HTTP client for the Stax REST API (no SDK).</summary>
public class StaxClient
{
    private readonly HttpClient _http;
    private readonly StaxOptions _options;

    public StaxClient(HttpClient http, IOptions<StaxOptions> options)
    {
        _http = http;
        _options = options.Value;
    }

    private string BaseUrl => string.IsNullOrWhiteSpace(_options.BaseUrl) ? StaxOptions.DEFAULT_BASE_URL : _options.BaseUrl!;

    public bool HasPlatformKey => !string.IsNullOrWhiteSpace(_options.PlatformApiKey);

    public Task<JsonNode?> GetAsync(string path, CancellationToken ct = default) => SendAsync(HttpMethod.Get, path, null, false, ct);
    public Task<JsonNode?> PostAsync(string path, JsonNode body, CancellationToken ct = default) => SendAsync(HttpMethod.Post, path, body, false, ct);
    public Task<JsonNode?> PutAsync(string path, JsonNode body, CancellationToken ct = default) => SendAsync(HttpMethod.Put, path, body, false, ct);
    public Task<JsonNode?> DeleteAsync(string path, CancellationToken ct = default) => SendAsync(HttpMethod.Delete, path, null, false, ct);
    public Task<JsonNode?> PlatformPostAsync(string path, JsonNode body, CancellationToken ct = default) => SendAsync(HttpMethod.Post, path, body, true, ct);

    /// <summary>Reads every page of a list endpoint (either a bare array or a paged {data,last_page} object).</summary>
    public async Task<List<JsonNode>> GetAllAsync(string path, CancellationToken ct = default)
    {
        var result = new List<JsonNode>();
        for (var page = 1; page <= 200; page++)
        {
            var sep = path.Contains('?') ? "&" : "?";
            var node = await GetAsync($"{path}{sep}page={page}&per_page=100", ct);
            if (node is JsonArray arr)
            {
                result.AddRange(arr.Where(n => n != null)!);
                break;
            }
            var data = node?["data"] as JsonArray;
            if (data != null) result.AddRange(data.Where(n => n != null)!);
            var last = node?["last_page"]?.GetValue<int>() ?? 1;
            if (page >= last || data == null || data.Count == 0) break;
        }
        return result;
    }

    private async Task<JsonNode?> SendAsync(HttpMethod method, string path, JsonNode? body, bool platform, CancellationToken ct)
    {
        var key = platform ? _options.PlatformApiKey : _options.ApiKey;
        if (string.IsNullOrWhiteSpace(key))
        {
            throw ApiException.Unprocessable(platform
                ? "Seller onboarding is not available: the shop has no Stax platform key configured (Stax:PlatformApiKey)."
                : "Payments are not available: no Stax API key is configured (Stax:ApiKey).");
        }

        using var request = new HttpRequestMessage(method, BaseUrl.TrimEnd('/') + "/" + path.TrimStart('/'));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (body != null)
        {
            request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        }

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new ApiException(502, "Stax could not be reached.", ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new ApiException(502, "Stax did not answer in time.", ex);
        }

        using (response)
        {
            var text = await response.Content.ReadAsStringAsync(ct);
            JsonNode? json = null;
            if (!string.IsNullOrWhiteSpace(text))
            {
                try { json = JsonNode.Parse(text); } catch (System.Text.Json.JsonException) { }
            }
            if (!response.IsSuccessStatusCode)
            {
                throw new StaxApiException((int)response.StatusCode, Describe(json, text));
            }
            return json;
        }
    }

    private static string Describe(JsonNode? json, string raw)
    {
        if (json is JsonObject obj)
        {
            var parts = new List<string>();
            Collect(obj, parts);
            if (parts.Count > 0) return string.Join("; ", parts.Distinct());
        }
        return string.IsNullOrWhiteSpace(raw) ? "request failed" : (raw.Length > 300 ? raw[..300] : raw);
    }

    private static void Collect(JsonNode? node, List<string> parts)
    {
        switch (node)
        {
            case JsonValue v when v.TryGetValue<string>(out var s):
                parts.Add(s);
                break;
            case JsonArray a:
                foreach (var i in a) Collect(i, parts);
                break;
            case JsonObject o:
                if (o["message"] is JsonValue m && m.TryGetValue<string>(out var ms)) { parts.Add(ms); return; }
                foreach (var kv in o) Collect(kv.Value, parts);
                break;
        }
    }
}
