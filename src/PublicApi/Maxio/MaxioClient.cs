using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Thin HTTP client over the Maxio Advanced Billing API (no SDK).
/// </summary>
public class MaxioClient
{
    private readonly HttpClient _http;

    public MaxioClient(HttpClient http, IOptions<MaxioSettings> options)
    {
        _http = http;
        var settings = options.Value;
        if (string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            // Deferred: reported as a 503 on first use so the app still starts without credentials.
            return;
        }

        _http.BaseAddress = ResolveBaseAddress(settings);
        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes(settings.ApiKey + ":x"));
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public static Uri? ResolveBaseAddress(MaxioSettings settings)
    {
        string url;
        if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
        {
            url = settings.BaseUrl.Trim();
        }
        else
        {
            if (string.IsNullOrWhiteSpace(settings.Subdomain))
            {
                return null;
            }
            var host = (settings.Environment ?? "US").Trim().ToUpperInvariant() switch
            {
                "US" => $"{settings.Subdomain}.chargify.com",
                "EU" => $"{settings.Subdomain}.ebilling.maxio.com",
                _ => throw new MaxioException(503, $"Unsupported Maxio:Environment '{settings.Environment}'.")
            };
            url = $"https://{host}";
        }
        // Only make sure relative paths append to (not replace) the last segment.
        return new Uri(url.EndsWith('/') ? url : url + "/", UriKind.Absolute);
    }

    public Task<JsonNode?> GetAsync(string path, CancellationToken ct = default) => SendAsync(HttpMethod.Get, path, null, ct);
    public Task<JsonNode?> PostAsync(string path, JsonNode? body, CancellationToken ct = default) => SendAsync(HttpMethod.Post, path, body, ct);
    public Task<JsonNode?> PutAsync(string path, JsonNode? body, CancellationToken ct = default) => SendAsync(HttpMethod.Put, path, body, ct);
    public Task<JsonNode?> DeleteAsync(string path, JsonNode? body, CancellationToken ct = default) => SendAsync(HttpMethod.Delete, path, body, ct);

    private async Task<JsonNode?> SendAsync(HttpMethod method, string path, JsonNode? body, CancellationToken ct)
    {
        if (_http.BaseAddress == null || _http.DefaultRequestHeaders.Authorization == null)
        {
            throw new MaxioException(503, "Billing is not configured (Maxio:ApiKey and Maxio:Subdomain or Maxio:BaseUrl are required).");
        }

        using var request = new HttpRequestMessage(method, path);
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
            throw new MaxioException(502, $"The billing system could not be reached: {ex.Message}");
        }

        using (response)
        {
            var text = await response.Content.ReadAsStringAsync(ct);
            if (response.IsSuccessStatusCode)
            {
                if (string.IsNullOrWhiteSpace(text))
                {
                    return null;
                }
                try
                {
                    return JsonNode.Parse(text);
                }
                catch (JsonException)
                {
                    throw new MaxioException(502, "The billing system returned an unreadable response.");
                }
            }

            var reason = ExtractReason(text);
            throw response.StatusCode switch
            {
                HttpStatusCode.NotFound => new MaxioException(404, reason ?? "Not found in the billing system."),
                HttpStatusCode.UnprocessableEntity or HttpStatusCode.BadRequest => new MaxioException(422, reason ?? "The billing system rejected the request."),
                HttpStatusCode.Conflict => new MaxioException(409, reason ?? "Conflict in the billing system."),
                HttpStatusCode.TooManyRequests => new MaxioException(503, "The billing system is rate limiting requests; try again shortly."),
                _ => new MaxioException(502, $"The billing system failed the request ({(int)response.StatusCode}).")
            };
        }
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
            var errors = node?["errors"];
            if (errors == null)
            {
                return null;
            }
            var parts = new System.Collections.Generic.List<string>();
            void Collect(JsonNode? n, string? key)
            {
                switch (n)
                {
                    case JsonArray arr:
                        foreach (var item in arr) Collect(item, key);
                        break;
                    case JsonObject obj:
                        foreach (var kv in obj) Collect(kv.Value, kv.Key);
                        break;
                    case null:
                        break;
                    default:
                        var s = n.ToString();
                        if (!parts.Contains(s))
                        {
                            parts.Add(key == null || key == "base" ? s : $"{key}: {s}");
                        }
                        break;
                }
            }
            Collect(errors, null);
            return parts.Count == 0 ? null : string.Join("; ", parts);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
