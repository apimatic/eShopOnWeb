using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Thin HTTP client for the Maxio Advanced Billing API (no SDK).
/// </summary>
public class MaxioClient
{
    private readonly HttpClient _http;
    private readonly MaxioSettings _settings;

    public MaxioClient(HttpClient http, IOptions<MaxioSettings> settings)
    {
        _http = http;
        _settings = settings.Value;
    }

    private string BaseAddress()
    {
        if (!string.IsNullOrWhiteSpace(_settings.BaseUrl))
        {
            return _settings.BaseUrl.TrimEnd('/');
        }
        if (string.IsNullOrWhiteSpace(_settings.Subdomain))
        {
            throw new MaxioApiException(503, "Billing is not configured (Maxio:Subdomain is missing).");
        }
        var host = string.Equals(_settings.Environment, "EU", StringComparison.OrdinalIgnoreCase)
            ? "ebilling.maxio.com"
            : "chargify.com";
        return $"https://{_settings.Subdomain}.{host}";
    }

    /// <summary>Sends a request; returns null when Maxio answers 404.</summary>
    public async Task<JsonNode?> SendAsync(HttpMethod method, string path, object? body = null, bool nullOn404 = false)
    {
        if (string.IsNullOrWhiteSpace(_settings.ApiKey))
        {
            throw new MaxioApiException(503, "Billing is not configured (Maxio:ApiKey is missing).");
        }

        var url = BaseAddress() + "/" + path.TrimStart('/');
        using var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes(_settings.ApiKey + ":x")));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (body != null)
        {
            request.Content = new StringContent(System.Text.Json.JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        }
        else if (method != HttpMethod.Get)
        {
            request.Content = new StringContent("{}", Encoding.UTF8, "application/json");
        }

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request);
        }
        catch (HttpRequestException ex)
        {
            throw new MaxioApiException(502, "The billing system could not be reached: " + ex.Message);
        }
        catch (TaskCanceledException)
        {
            throw new MaxioApiException(504, "The billing system timed out.");
        }

        using (response)
        {
            var text = await response.Content.ReadAsStringAsync();
            if (response.IsSuccessStatusCode)
            {
                return string.IsNullOrWhiteSpace(text) ? null : JsonNode.Parse(text);
            }
            if (response.StatusCode == HttpStatusCode.NotFound && nullOn404)
            {
                return null;
            }
            var code = (int)response.StatusCode;
            var message = ExtractError(text) ?? $"The billing system answered {code}.";
            if (code >= 500 || code == 401 || code == 403 || code == 429)
            {
                // Not the caller's fault; report as a gateway problem.
                throw new MaxioApiException(code == 429 ? 503 : 502, "Billing system error: " + message);
            }
            throw new MaxioApiException(code == 404 ? 404 : 422, message);
        }
    }

    public Task<JsonNode?> GetAsync(string path, bool nullOn404 = false) => SendAsync(HttpMethod.Get, path, null, nullOn404);

    /// <summary>Reads every page of a list endpoint (Maxio returns a bare JSON array).</summary>
    public async Task<List<JsonNode>> GetAllPagesAsync(string path)
    {
        const int perPage = 200;
        var all = new List<JsonNode>();
        for (var page = 1; page <= 500; page++)
        {
            var sep = path.Contains('?') ? "&" : "?";
            var node = await GetAsync($"{path}{sep}per_page={perPage}&page={page}");
            var arr = node as JsonArray;
            if (arr == null) break;
            foreach (var item in arr)
            {
                if (item != null) all.Add(item);
            }
            if (arr.Count < perPage) break;
        }
        return all;
    }

    private static string? ExtractError(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        try
        {
            var node = JsonNode.Parse(text);
            var errors = node?["errors"];
            var parts = new List<string>();
            Collect(errors, parts);
            return parts.Count > 0 ? string.Join(" ", parts) : null;
        }
        catch
        {
            return null;
        }
    }

    private static void Collect(JsonNode? node, List<string> parts)
    {
        switch (node)
        {
            case JsonArray arr:
                foreach (var n in arr) Collect(n, parts);
                break;
            case JsonObject obj:
                foreach (var kv in obj) Collect(kv.Value, parts);
                break;
            case JsonValue v:
                var s = v.ToString();
                if (!string.IsNullOrWhiteSpace(s) && !parts.Contains(s)) parts.Add(s);
                break;
        }
    }
}
