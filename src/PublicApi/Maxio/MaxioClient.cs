using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Thin HTTP client for the Maxio (Advanced Billing) REST API. No SDK is used.
/// </summary>
public class MaxioClient
{
    private const int PAGE_SIZE = 200;
    private readonly HttpClient _http;
    private readonly MaxioSettings _settings;

    public MaxioClient(HttpClient http, IOptions<MaxioSettings> settings)
    {
        _http = http;
        _settings = settings.Value;
    }

    private string BuildBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(_settings.BaseUrl))
        {
            return _settings.BaseUrl.Trim();
        }

        if (string.IsNullOrWhiteSpace(_settings.Subdomain))
        {
            throw new InvalidOperationException("Maxio:Subdomain is not configured.");
        }

        var host = (_settings.Environment ?? "US").Trim().ToUpperInvariant() switch
        {
            "US" => $"{_settings.Subdomain}.chargify.com",
            "EU" => $"{_settings.Subdomain}.ebilling.maxio.com",
            var other => throw new InvalidOperationException($"Maxio:Environment '{other}' is not supported (use US or EU).")
        };
        return $"https://{host}";
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string path, JsonNode? body)
    {
        if (string.IsNullOrWhiteSpace(_settings.ApiKey))
        {
            throw new InvalidOperationException("Maxio:ApiKey is not configured.");
        }

        var request = new HttpRequestMessage(method, BuildBaseUrl().TrimEnd('/') + "/" + path.TrimStart('/'));
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes(_settings.ApiKey + ":x"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (body != null)
        {
            request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        }
        return request;
    }

    /// <summary>Sends a request; a 404 answer returns null.</summary>
    public async Task<JsonNode?> SendOrNullAsync(HttpMethod method, string path, JsonNode? body = null)
    {
        try
        {
            using var request = CreateRequest(method, path, body);
            using var response = await _http.SendAsync(request);
            var text = await response.Content.ReadAsStringAsync();
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }
            if (!response.IsSuccessStatusCode)
            {
                throw ToException(response.StatusCode, text);
            }
            return string.IsNullOrWhiteSpace(text) ? new JsonObject() : JsonNode.Parse(text);
        }
        catch (HttpRequestException ex)
        {
            throw new MaxioApiException(HttpStatusCode.BadGateway, $"The billing system could not be reached: {ex.Message}");
        }
        catch (TaskCanceledException)
        {
            throw new MaxioApiException(HttpStatusCode.GatewayTimeout, "The billing system did not answer in time.");
        }
    }

    /// <summary>Sends a request; a 404 answer is an error with the given reason.</summary>
    public async Task<JsonNode> SendAsync(HttpMethod method, string path, JsonNode? body = null, string notFound = "Not found.")
    {
        return await SendOrNullAsync(method, path, body)
            ?? throw new MaxioApiException(HttpStatusCode.NotFound, notFound);
    }

    /// <summary>Reads every page of a list endpoint (Maxio answers with a JSON array).</summary>
    public async Task<List<JsonNode>> ListAllAsync(string path)
    {
        var all = new List<JsonNode>();
        for (var page = 1; ; page++)
        {
            var sep = path.Contains('?') ? "&" : "?";
            var node = await SendAsync(HttpMethod.Get, $"{path}{sep}per_page={PAGE_SIZE}&page={page}");
            var items = node as JsonArray;
            if (items == null || items.Count == 0)
            {
                break;
            }
            all.AddRange(items.Where(i => i != null).Select(i => i!));
            if (items.Count < PAGE_SIZE)
            {
                break;
            }
        }
        return all;
    }

    private static MaxioApiException ToException(HttpStatusCode status, string text)
    {
        var reason = ExtractReason(text);
        if ((int)status >= 500)
        {
            return new MaxioApiException(HttpStatusCode.BadGateway, $"The billing system failed: {reason}");
        }
        return status switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
                new MaxioApiException(HttpStatusCode.BadGateway, $"The billing system rejected the app's credentials: {reason}"),
            HttpStatusCode.TooManyRequests => new MaxioApiException(HttpStatusCode.TooManyRequests, reason),
            _ => new MaxioApiException(HttpStatusCode.UnprocessableEntity, reason)
        };
    }

    private static string ExtractReason(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "The billing system refused the request.";
        }
        try
        {
            var node = JsonNode.Parse(text);
            var errors = (node as JsonObject)?["errors"] ?? (node as JsonObject)?["error"];
            var parts = new List<string>();
            Collect(errors ?? node, parts);
            var distinct = parts.Where(p => !string.IsNullOrWhiteSpace(p)).Distinct().ToList();
            if (distinct.Count > 0)
            {
                return string.Join(" ", distinct);
            }
        }
        catch (JsonException)
        {
        }
        return text.Length > 300 ? text[..300] : text;
    }

    private static void Collect(JsonNode? node, List<string> parts)
    {
        switch (node)
        {
            case JsonValue v:
                parts.Add(v.ToString());
                break;
            case JsonArray a:
                foreach (var i in a) Collect(i, parts);
                break;
            case JsonObject o:
                foreach (var p in o) Collect(p.Value, parts);
                break;
        }
    }
}
