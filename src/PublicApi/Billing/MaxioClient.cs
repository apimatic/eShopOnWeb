using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Billing;

/// <summary>
/// Thin HTTP client for the Maxio (Advanced Billing) REST API. No SDK is used.
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
            return _settings.BaseUrl;
        }

        if (string.IsNullOrWhiteSpace(_settings.Subdomain))
        {
            throw new BillingException(HttpStatusCode.ServiceUnavailable, "Billing is not configured (Maxio:Subdomain is missing).");
        }

        var host = string.Equals(_settings.Environment, "EU", StringComparison.OrdinalIgnoreCase)
            ? $"{_settings.Subdomain}.eu.chargify.com"
            : $"{_settings.Subdomain}.chargify.com";
        return $"https://{host}";
    }

    public static string Segment(string value) => Uri.EscapeDataString(value);

    /// <summary>Sends a request; returns the parsed JSON body (null for an empty body).</summary>
    public async Task<JsonNode?> SendAsync(HttpMethod method, string path, object? body = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_settings.ApiKey))
        {
            throw new BillingException(HttpStatusCode.ServiceUnavailable, "Billing is not configured (Maxio:ApiKey is missing).");
        }

        var url = BaseAddress().TrimEnd('/') + "/" + path.TrimStart('/');
        using var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes(_settings.ApiKey + ":x")));
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
        catch (HttpRequestException ex)
        {
            throw new BillingException(HttpStatusCode.BadGateway, "The billing system could not be reached: " + ex.Message);
        }

        using (response)
        {
            var text = await response.Content.ReadAsStringAsync(ct);
            if (response.IsSuccessStatusCode)
            {
                return string.IsNullOrWhiteSpace(text) ? null : JsonNode.Parse(text);
            }

            throw MapError(response.StatusCode, text);
        }
    }

    private static BillingException MapError(HttpStatusCode status, string text)
    {
        var reason = ExtractReason(text);
        switch ((int)status)
        {
            case 404:
                return new BillingException(HttpStatusCode.NotFound, reason ?? "Not found in the billing system.");
            case 400:
            case 422:
            case 409:
                return new BillingException((HttpStatusCode)422, reason ?? "The billing system rejected the request.");
            case 429:
                return new BillingException(HttpStatusCode.TooManyRequests, "The billing system is rate limiting requests; try again shortly.");
            default:
                // 401/403 mean our own credentials are wrong, 5xx is Maxio failing: neither is the caller's fault.
                return new BillingException(HttpStatusCode.BadGateway, $"The billing system failed the request ({(int)status}).");
        }
    }

    private static string? ExtractReason(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        try
        {
            var node = JsonNode.Parse(text);
            var errors = node?["errors"] ?? node?["error"];
            if (errors == null) return null;
            var parts = new List<string>();
            Flatten(errors, parts);
            return parts.Count == 0 ? null : string.Join(" ", parts.Distinct());
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static void Flatten(JsonNode node, List<string> parts)
    {
        switch (node)
        {
            case JsonArray array:
                foreach (var item in array) { if (item != null) Flatten(item, parts); }
                break;
            case JsonObject obj:
                foreach (var kv in obj) { if (kv.Value != null) Flatten(kv.Value, parts); }
                break;
            default:
                parts.Add(node.ToString());
                break;
        }
    }

    /// <summary>Reads every page of a list endpoint that returns a JSON array.</summary>
    public async Task<List<JsonNode>> ListAllAsync(string path, CancellationToken ct = default)
    {
        const int perPage = 200;
        var all = new List<JsonNode>();
        for (var page = 1; page < 1000; page++)
        {
            var sep = path.Contains('?') ? "&" : "?";
            var node = await SendAsync(HttpMethod.Get, $"{path}{sep}per_page={perPage}&page={page}", null, ct);
            var items = node as JsonArray ?? node?["invoices"] as JsonArray;
            if (items == null || items.Count == 0) break;
            all.AddRange(items.Where(i => i != null)!);
            if (items.Count < perPage) break;
        }
        return all;
    }
}
