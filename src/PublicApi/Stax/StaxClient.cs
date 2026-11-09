using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;

namespace Microsoft.eShopWeb.PublicApi.Stax;

/// <summary>
/// Thin JSON-over-HTTP client for the Stax API. No SDK is used.
/// Settings come from the "Stax:" configuration section (ApiKey, PlatformApiKey, BaseUrl).
/// </summary>
public class StaxClient
{
    public const string DefaultBaseUrl = "https://apiprod.fattlabs.com";

    private readonly HttpClient _http;
    private readonly IConfiguration _configuration;

    public StaxClient(HttpClient http, IConfiguration configuration)
    {
        _http = http;
        _configuration = configuration;
    }

    public bool HasApiKey => !string.IsNullOrWhiteSpace(_configuration["Stax:ApiKey"]);

    private string BaseUrl
    {
        get
        {
            var configured = _configuration["Stax:BaseUrl"];
            // an explicit override is used verbatim as the API's base address
            return string.IsNullOrWhiteSpace(configured) ? DefaultBaseUrl : configured;
        }
    }

    public Task<JsonNode> GetAsync(string path, CancellationToken ct = default) => SendAsync(HttpMethod.Get, path, null, false, ct);
    public Task<JsonNode> PostAsync(string path, object? body, CancellationToken ct = default) => SendAsync(HttpMethod.Post, path, body ?? new { }, false, ct);
    public Task<JsonNode> PutAsync(string path, object body, CancellationToken ct = default) => SendAsync(HttpMethod.Put, path, body, false, ct);
    public Task<JsonNode> DeleteAsync(string path, CancellationToken ct = default) => SendAsync(HttpMethod.Delete, path, null, false, ct);
    public Task<JsonNode> PostPlatformAsync(string path, object body, CancellationToken ct = default) => SendAsync(HttpMethod.Post, path, body, true, ct);

    /// <summary>Gets every row of a list endpoint (Stax paginates with current_page / last_page).</summary>
    public async Task<List<JsonNode>> GetAllAsync(string path, CancellationToken ct = default)
    {
        var all = new List<JsonNode>();
        var sep = path.Contains('?') ? "&" : "?";
        for (var page = 1; page <= 50; page++)
        {
            var node = await GetAsync($"{path}{sep}per_page=100&page={page}", ct);
            if (node is JsonArray arr)
            {
                all.AddRange(arr.Where(x => x != null).Select(x => x!));
                break;
            }
            var data = node["data"] as JsonArray;
            if (data != null) all.AddRange(data.Where(x => x != null).Select(x => x!));
            var last = node["last_page"]?.GetValue<int?>() ?? 1;
            if (page >= last || data == null || data.Count == 0) break;
        }
        return all;
    }

    private async Task<JsonNode> SendAsync(HttpMethod method, string path, object? body, bool platform, CancellationToken ct)
    {
        var key = _configuration[platform ? "Stax:PlatformApiKey" : "Stax:ApiKey"];
        if (string.IsNullOrWhiteSpace(key))
        {
            throw platform
                ? ApiException.Unprocessable("The shop has no Stax platform API key configured (Stax:PlatformApiKey), so this cannot be carried out.")
                : new ApiException(503, "The shop's Stax API key (Stax:ApiKey) is not configured.");
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
        try
        {
            response = await _http.SendAsync(request, ct);
        }
        catch (Exception ex) when ((ex is HttpRequestException || ex is TaskCanceledException) && !ct.IsCancellationRequested)
        {
            throw new ApiException(502, "Stax could not be reached: " + ex.Message);
        }

        using (response)
        {
            var text = await response.Content.ReadAsStringAsync(ct);
            JsonNode? node = null;
            if (!string.IsNullOrWhiteSpace(text))
            {
                try { node = JsonNode.Parse(text); } catch (JsonException) { }
            }

            if (response.IsSuccessStatusCode)
            {
                return node ?? new JsonObject();
            }

            throw new StaxException((int)response.StatusCode, Describe(node, text), MapStatus(response.StatusCode));
        }
    }

    private static int MapStatus(HttpStatusCode code) => (int)code switch
    {
        400 or 422 => 422,
        404 => 404,
        409 => 409,
        429 => 429,
        _ => 502
    };

    /// <summary>Flattens the various Stax error body shapes into one readable reason.</summary>
    private static string Describe(JsonNode? node, string raw)
    {
        var parts = new List<string>();
        void Walk(JsonNode? n)
        {
            switch (n)
            {
                case JsonValue v when v.TryGetValue<string>(out var s):
                    if (!parts.Contains(s)) parts.Add(s);
                    break;
                case JsonObject o:
                    foreach (var kv in o) Walk(kv.Value);
                    break;
                case JsonArray a:
                    foreach (var item in a) Walk(item);
                    break;
            }
        }
        if (node is JsonObject obj)
        {
            foreach (var kv in obj)
            {
                if (kv.Key is "success" or "status") continue;
                Walk(kv.Value);
            }
        }
        else if (node != null) Walk(node);
        return parts.Count > 0 ? string.Join("; ", parts) : (string.IsNullOrWhiteSpace(raw) ? "Stax returned an error." : raw);
    }
}

/// <summary>A Stax call that Stax rejected. <see cref="ApiException.StatusCode"/> is the status to answer our caller with.</summary>
public class StaxException : ApiException
{
    public int StaxStatus { get; }

    public StaxException(int staxStatus, string message, int status) : base(status, message)
    {
        StaxStatus = staxStatus;
    }
}

public static class JsonNodeExtensions
{
    public static string? Str(this JsonNode? node, string key)
    {
        var v = node?[key];
        if (v == null) return null;
        return v is JsonValue jv && jv.TryGetValue<string>(out var s) ? s : v.ToString();
    }

    public static decimal Dec(this JsonNode? node, string key)
    {
        var v = node?[key];
        if (v is JsonValue jv)
        {
            if (jv.TryGetValue<decimal>(out var d)) return d;
            if (jv.TryGetValue<string>(out var s) && decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var p)) return p;
        }
        return 0m;
    }

    public static bool Bool(this JsonNode? node, string key)
    {
        var v = node?[key];
        if (v is JsonValue jv)
        {
            if (jv.TryGetValue<bool>(out var b)) return b;
            if (jv.TryGetValue<int>(out var i)) return i != 0;
        }
        return false;
    }

    /// <summary>Stax timestamps look like "2026-11-09 15:02:45" and are UTC.</summary>
    public static DateTimeOffset? Time(this JsonNode? node, string key)
    {
        var s = node.Str(key);
        if (string.IsNullOrWhiteSpace(s)) return null;
        return DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var t) ? t : (DateTimeOffset?)null;
    }
}
