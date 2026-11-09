using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Stax;

/// <summary>
/// Thin client for the Stax HTTP API. No SDK; every call is a plain HTTP request.
/// </summary>
public class StaxClient
{
    private readonly HttpClient _http;
    private readonly StaxOptions _options;

    public StaxClient(HttpClient http, IOptions<StaxOptions> options)
    {
        _http = http;
        _options = options.Value;
    }

    public bool HasPlatformKey => !string.IsNullOrWhiteSpace(_options.PlatformApiKey);

    public Task<JsonNode> GetAsync(string path, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Get, path, null, false, ct);

    public Task<JsonNode> PostAsync(string path, JsonNode? body, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Post, path, body, false, ct);

    public Task<JsonNode> PutAsync(string path, JsonNode? body, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Put, path, body, false, ct);

    public Task<JsonNode> DeleteAsync(string path, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Delete, path, null, false, ct);

    /// <summary>Calls Stax with the partner (platform) key instead of the merchant key.</summary>
    public Task<JsonNode> PostAsPlatformAsync(string path, JsonNode? body, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Post, path, body, true, ct);

    private string BuildUrl(string path)
    {
        var baseUrl = string.IsNullOrWhiteSpace(_options.BaseUrl) ? StaxOptions.DefaultBaseUrl : _options.BaseUrl!;
        return baseUrl.TrimEnd('/') + "/" + path.TrimStart('/');
    }

    private async Task<JsonNode> SendAsync(HttpMethod method, string path, JsonNode? body, bool platform, CancellationToken ct)
    {
        var key = platform ? _options.PlatformApiKey : _options.ApiKey;
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ApiException(503, platform
                ? "Stax:PlatformApiKey is not configured."
                : "Stax:ApiKey is not configured.");
        }

        using var request = new HttpRequestMessage(method, BuildUrl(path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        string text;
        try
        {
            using var response = await _http.SendAsync(request, ct);
            text = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
            {
                throw new StaxException((int)response.StatusCode, ExtractMessage(text, (int)response.StatusCode));
            }
        }
        catch (HttpRequestException ex)
        {
            throw new StaxException(0, "Stax could not be reached.", ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new StaxException(0, "Stax did not answer in time.", ex);
        }

        if (string.IsNullOrWhiteSpace(text)) return new JsonObject();
        try
        {
            return JsonNode.Parse(text) ?? new JsonObject();
        }
        catch (System.Text.Json.JsonException ex)
        {
            throw new StaxException(502, "Stax answered with something unreadable.", ex);
        }
    }

    /// <summary>Flattens Stax's various error shapes ({"field":["msg"]}, {"message":".."}) into one sentence.</summary>
    private static string ExtractMessage(string text, int status)
    {
        try
        {
            if (JsonNode.Parse(text) is JsonObject obj)
            {
                var parts = new StringBuilder();
                foreach (var (name, value) in obj)
                {
                    var msg = value is JsonArray arr
                        ? string.Join(" ", arr.Select(a => a?.ToString()))
                        : value?.ToString();
                    if (string.IsNullOrWhiteSpace(msg)) continue;
                    if (parts.Length > 0) parts.Append(' ');
                    parts.Append(name is "message" or "error" ? msg : $"{name}: {msg}");
                }
                if (parts.Length > 0) return parts.ToString();
            }
        }
        catch (System.Text.Json.JsonException)
        {
            // fall through to the generic message
        }
        return $"Stax answered with status {status}.";
    }
}
