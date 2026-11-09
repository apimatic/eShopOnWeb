using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Stax;

/// <summary>Thin wrapper over the Stax REST API (plain HTTP, no SDK).</summary>
public class StaxClient
{
    private readonly HttpClient _http;
    private readonly StaxOptions _options;

    public StaxClient(HttpClient http, IOptions<StaxOptions> options)
    {
        _http = http;
        _options = options.Value;
    }

    private string BaseUrl => string.IsNullOrWhiteSpace(_options.BaseUrl) ? StaxOptions.DefaultBaseUrl : _options.BaseUrl!;
    public string InvoiceUrl => _options.InvoiceUrl;

    public Task<JsonNode> GetAsync(string path, CancellationToken ct = default) => SendAsync(HttpMethod.Get, path, null, false, ct);
    public Task<JsonNode> PostAsync(string path, object? body, CancellationToken ct = default) => SendAsync(HttpMethod.Post, path, body, false, ct);
    public Task<JsonNode> PutAsync(string path, object? body, CancellationToken ct = default) => SendAsync(HttpMethod.Put, path, body, false, ct);
    public Task<JsonNode> DeleteAsync(string path, CancellationToken ct = default) => SendAsync(HttpMethod.Delete, path, null, false, ct);
    public Task<JsonNode> PostAsPlatformAsync(string path, object? body, CancellationToken ct = default) => SendAsync(HttpMethod.Post, path, body, true, ct);

    private async Task<JsonNode> SendAsync(HttpMethod method, string path, object? body, bool platform, CancellationToken ct)
    {
        var key = platform ? _options.PlatformApiKey : _options.ApiKey;
        if (string.IsNullOrWhiteSpace(key))
        {
            throw ApiException.Unprocessable(platform
                ? "Seller onboarding is unavailable: no Stax platform API key is configured (Stax:PlatformApiKey)."
                : "Payments are unavailable: no Stax API key is configured (Stax:ApiKey).");
        }

        var url = BaseUrl.TrimEnd('/') + "/" + path.TrimStart('/');
        using var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (body != null)
        {
            request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        }

        HttpResponseMessage response;
        string text;
        try
        {
            response = await _http.SendAsync(request, ct);
            text = await response.Content.ReadAsStringAsync(ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new StaxException(503, "Stax could not be reached: " + ex.Message);
        }

        JsonNode? json = null;
        if (!string.IsNullOrWhiteSpace(text))
        {
            try { json = JsonNode.Parse(text); } catch (JsonException) { }
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new StaxException((int)response.StatusCode, DescribeError((int)response.StatusCode, json, text));
        }
        return json ?? new JsonObject();
    }

    /// <summary>Flattens Stax's various error shapes into one readable sentence.</summary>
    public static string DescribeError(int status, JsonNode? json, string raw)
    {
        var parts = new List<string>();
        void Walk(JsonNode? n, string? name)
        {
            switch (n)
            {
                case JsonValue v when v.TryGetValue<string>(out var s):
                    parts.Add(name is null || name is "error" or "message" || int.TryParse(name, out _) ? s : $"{name}: {s}");
                    break;
                case JsonObject o:
                    foreach (var kv in o) Walk(kv.Value, kv.Key);
                    break;
                case JsonArray a:
                    foreach (var i in a) Walk(i, name);
                    break;
            }
        }
        Walk(json, null);
        var distinct = new List<string>();
        foreach (var p in parts) if (!distinct.Contains(p)) distinct.Add(p);
        var msg = distinct.Count > 0 ? string.Join("; ", distinct) : (raw.Length > 200 ? raw[..200] : raw);
        return string.IsNullOrWhiteSpace(msg) ? $"Stax returned HTTP {status}." : $"Stax: {msg}";
    }

    // ---- tiny JSON helpers -------------------------------------------------
    public static string? Str(JsonNode? n, string name) =>
        n?[name] is JsonValue v ? (v.TryGetValue<string>(out var s) ? s : v.ToString()) : null;

    public static decimal Dec(JsonNode? n, string name)
    {
        if (n?[name] is JsonValue v)
        {
            if (v.TryGetValue<decimal>(out var d)) return d;
            if (v.TryGetValue<double>(out var f)) return (decimal)f;
            if (v.TryGetValue<string>(out var s) && decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var p)) return p;
        }
        return 0m;
    }

    public static bool Bool(JsonNode? n, string name) =>
        n?[name] is JsonValue v && (v.TryGetValue<bool>(out var b) ? b : (v.TryGetValue<int>(out var i) && i != 0));

    /// <summary>Pages through a Stax list endpoint ({ data: [...], last_page }).</summary>
    public async Task<List<JsonNode>> ListAllAsync(string path, CancellationToken ct = default)
    {
        var all = new List<JsonNode>();
        var sep = path.Contains('?') ? "&" : "?";
        for (var page = 1; page <= 500; page++)
        {
            var res = await GetAsync($"{path}{sep}per_page=100&page={page}", ct);
            if (res is JsonArray arr)
            {
                foreach (var i in arr) if (i != null) all.Add(i);
                break;
            }
            if (res["data"] is JsonArray data) foreach (var i in data) if (i != null) all.Add(i);
            var last = res["last_page"] is JsonValue lv && lv.TryGetValue<int>(out var l) ? l : 1;
            if (page >= last) break;
        }
        return all;
    }
}
