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
            return _settings.BaseUrl;
        }
        if (string.IsNullOrWhiteSpace(_settings.Subdomain))
        {
            throw new BillingException(503, "Billing is not configured (Maxio:Subdomain is missing).");
        }
        var host = string.Equals(_settings.Environment, "EU", StringComparison.OrdinalIgnoreCase)
            ? "ebilling.maxio.com"
            : "chargify.com";
        return $"https://{_settings.Subdomain}.{host}";
    }

    /// <summary>
    /// Sends a request. Returns null when Maxio answers 404 and nullOnNotFound is set.
    /// Any other failure is raised as a BillingException carrying Maxio's reason.
    /// </summary>
    public async Task<JsonNode?> SendAsync(HttpMethod method, string path, object? body = null,
        bool nullOnNotFound = false, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_settings.ApiKey))
        {
            throw new BillingException(503, "Billing is not configured (Maxio:ApiKey is missing).");
        }

        var baseAddress = BaseAddress();
        var url = baseAddress.EndsWith('/') ? baseAddress + path : baseAddress + "/" + path;

        using var request = new HttpRequestMessage(method, url);
        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes(_settings.ApiKey + ":x"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
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
        catch (HttpRequestException ex)
        {
            throw new BillingException(502, "The billing system could not be reached: " + ex.Message);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new BillingException(504, "The billing system did not respond in time.");
        }

        using (response)
        {
            if (response.IsSuccessStatusCode)
            {
                return string.IsNullOrWhiteSpace(text) ? null : JsonNode.Parse(text);
            }

            if (response.StatusCode == HttpStatusCode.NotFound && nullOnNotFound)
            {
                return null;
            }

            var reason = ExtractReason(text);
            throw response.StatusCode switch
            {
                HttpStatusCode.NotFound => new BillingException(404, reason ?? "Not found in the billing system."),
                HttpStatusCode.UnprocessableEntity or HttpStatusCode.BadRequest or HttpStatusCode.Conflict
                    => new BillingException(422, reason ?? "The billing system rejected the request."),
                HttpStatusCode.TooManyRequests => new BillingException(503, "The billing system is rate limiting requests; try again shortly."),
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
                    => new BillingException(502, "The billing system rejected the app's credentials."),
                _ => new BillingException(502, reason ?? ("The billing system failed (" + (int)response.StatusCode + ")."))
            };
        }
    }

    /// <summary>Reads every page of a list endpoint.</summary>
    public async Task<List<JsonNode>> ListAllAsync(string path, CancellationToken ct = default)
    {
        const int perPage = 200;
        var all = new List<JsonNode>();
        for (var page = 1; page < 500; page++)
        {
            var sep = path.Contains('?') ? "&" : "?";
            var node = await SendAsync(HttpMethod.Get, path + sep + "per_page=" + perPage + "&page=" + page, ct: ct);
            var items = node as JsonArray;
            if (items == null && node is JsonObject obj)
            {
                // Some list endpoints wrap the array in an object (e.g. invoices).
                items = obj.Select(kv => kv.Value).OfType<JsonArray>().FirstOrDefault();
            }
            if (items == null || items.Count == 0)
            {
                break;
            }
            all.AddRange(items.Where(i => i != null)!);
            if (items.Count < perPage)
            {
                break;
            }
        }
        return all;
    }

    private static string? ExtractReason(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }
        try
        {
            var node = JsonNode.Parse(text);
            var errors = node?["errors"] ?? node?["error"];
            if (errors == null)
            {
                return null;
            }
            var messages = new List<string>();
            Collect(errors, messages);
            return messages.Count == 0 ? null : string.Join(" ", messages.Distinct());
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static void Collect(JsonNode node, List<string> messages)
    {
        switch (node)
        {
            case JsonArray arr:
                foreach (var n in arr) { if (n != null) Collect(n, messages); }
                break;
            case JsonObject obj:
                foreach (var kv in obj) { if (kv.Value != null) Collect(kv.Value, messages); }
                break;
            default:
                var s = node.ToString();
                if (!string.IsNullOrWhiteSpace(s)) messages.Add(s);
                break;
        }
    }
}
