using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Billing;

/// <summary>
/// Thin HTTP client for the Maxio (Advanced Billing) API. No SDK is used.
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

    private void Configure()
    {
        if (string.IsNullOrWhiteSpace(_settings.ApiKey))
        {
            throw new BillingException(503, "Billing is not configured (Maxio:ApiKey is missing).");
        }
        if (_http.BaseAddress == null)
        {
            _http.BaseAddress = _settings.ResolveBaseAddress();
            // Maxio authenticates with the API key as basic-auth user name and any password.
            var raw = Encoding.ASCII.GetBytes(_settings.ApiKey + ":x");
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(raw));
            _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        }
    }

    public Task<JsonNode?> GetAsync(string path) => SendAsync(HttpMethod.Get, path, null);
    public Task<JsonNode?> PostAsync(string path, JsonNode? body = null) => SendAsync(HttpMethod.Post, path, body ?? new JsonObject());
    public Task<JsonNode?> PutAsync(string path, JsonNode? body = null) => SendAsync(HttpMethod.Put, path, body ?? new JsonObject());
    public Task<JsonNode?> DeleteAsync(string path) => SendAsync(HttpMethod.Delete, path, new JsonObject());

    /// <summary>Gets every page of a list endpoint.</summary>
    public async Task<List<JsonNode>> GetAllPagesAsync(string path, int perPage = 200)
    {
        var all = new List<JsonNode>();
        for (var page = 1; page < 1000; page++)
        {
            var sep = path.Contains('?') ? "&" : "?";
            var result = await GetAsync(path + sep + "per_page=" + perPage + "&page=" + page);
            if (result is not JsonArray items) break;
            all.AddRange(items.Where(i => i != null).Select(i => i!));
            if (items.Count < perPage) break;
        }
        return all;
    }

    /// <summary>Like GetAsync but returns null on a Maxio 404.</summary>
    public async Task<JsonNode?> TryGetAsync(string path)
    {
        try
        {
            return await GetAsync(path);
        }
        catch (BillingException ex) when (ex.StatusCode == 404)
        {
            return null;
        }
    }

    private async Task<JsonNode?> SendAsync(HttpMethod method, string path, JsonNode? body)
    {
        Configure();
        using var request = new HttpRequestMessage(method, path);
        if (body != null)
        {
            request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        }

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new BillingException(502, "The billing system could not be reached.");
        }

        using (response)
        {
            var text = await response.Content.ReadAsStringAsync();
            if (response.IsSuccessStatusCode)
            {
                if (string.IsNullOrWhiteSpace(text)) return null;
                try { return JsonNode.Parse(text); }
                catch (System.Text.Json.JsonException) { throw new BillingException(502, "The billing system returned an unreadable response."); }
            }

            var reason = ExtractErrors(text);
            throw response.StatusCode switch
            {
                HttpStatusCode.NotFound => new BillingException(404, "Not found in the billing system."),
                HttpStatusCode.UnprocessableEntity => new BillingException(422, reason ?? "The billing system rejected the request."),
                HttpStatusCode.BadRequest => new BillingException(400, reason ?? "The billing system rejected the request."),
                HttpStatusCode.Conflict => new BillingException(409, reason ?? "The billing system rejected the request."),
                HttpStatusCode.TooManyRequests => new BillingException(503, "The billing system is busy, try again shortly."),
                // 401/403/5xx are not the caller's fault.
                _ => new BillingException(502, "The billing system failed the request (" + (int)response.StatusCode + ").")
            };
        }
    }

    private static string? ExtractErrors(string text)
    {
        try
        {
            var node = JsonNode.Parse(text);
            var errors = node?["errors"] ?? node;
            var messages = new List<string>();
            Collect(errors, messages);
            return messages.Count == 0 ? null : string.Join(" ", messages.Distinct());
        }
        catch
        {
            return null;
        }
    }

    private static void Collect(JsonNode? node, List<string> messages)
    {
        switch (node)
        {
            case JsonArray arr:
                foreach (var n in arr) Collect(n, messages);
                break;
            case JsonObject obj:
                foreach (var kv in obj) Collect(kv.Value, messages);
                break;
            case JsonValue v when v.TryGetValue<string>(out var s):
                messages.Add(s);
                break;
        }
    }
}
