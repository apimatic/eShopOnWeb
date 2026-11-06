using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Thin HTTP client for the Maxio (Advanced Billing) API. Failures surface as <see cref="ApiException"/>.
/// </summary>
public class MaxioClient
{
    private const int MAX_ATTEMPTS = 4;

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
            return _settings.BaseUrl!.Trim();
        }

        if (string.IsNullOrWhiteSpace(_settings.Subdomain))
        {
            throw new ApiException(503, "Billing is not configured (Maxio:Subdomain is missing).");
        }

        return (_settings.Environment ?? "").Trim().ToUpperInvariant() switch
        {
            "US" or "" => $"https://{_settings.Subdomain}.chargify.com",
            "EU" => $"https://{_settings.Subdomain}.ebilling.maxio.com",
            _ => throw new ApiException(503, $"Billing is not configured (unknown Maxio:Environment '{_settings.Environment}').")
        };
    }

    private Uri BuildUri(string path)
    {
        return new Uri(BaseAddress().TrimEnd('/') + "/" + path.TrimStart('/'));
    }

    public Task<JsonElement> GetAsync(string path, CancellationToken ct = default) => SendAsync(HttpMethod.Get, path, null, ct);
    public Task<JsonElement> PostAsync(string path, object? body, CancellationToken ct = default) => SendAsync(HttpMethod.Post, path, body ?? new { }, ct);
    public Task<JsonElement> PutAsync(string path, object? body, CancellationToken ct = default) => SendAsync(HttpMethod.Put, path, body ?? new { }, ct);
    public Task<JsonElement> DeleteAsync(string path, object? body, CancellationToken ct = default) => SendAsync(HttpMethod.Delete, path, body, ct);

    /// <summary>Gets every page of a list endpoint that returns a bare JSON array (or an object wrapping one under <paramref name="arrayProperty"/>).</summary>
    public async Task<List<JsonElement>> GetAllAsync(string path, string? arrayProperty = null, int perPage = 200, CancellationToken ct = default)
    {
        var all = new List<JsonElement>();
        for (var page = 1; page < 1000; page++)
        {
            var sep = path.Contains('?') ? '&' : '?';
            var json = await GetAsync($"{path}{sep}page={page}&per_page={perPage}", ct);
            var array = arrayProperty != null && json.ValueKind == JsonValueKind.Object ? json.GetProperty(arrayProperty) : json;
            if (array.ValueKind != JsonValueKind.Array)
            {
                break;
            }

            var count = 0;
            foreach (var item in array.EnumerateArray())
            {
                all.Add(item);
                count++;
            }

            if (count < perPage)
            {
                break;
            }
        }

        return all;
    }

    private async Task<JsonElement> SendAsync(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_settings.ApiKey))
        {
            throw new ApiException(503, "Billing is not configured (Maxio:ApiKey is missing).");
        }

        var uri = BuildUri(path);
        var auth = Convert.ToBase64String(Encoding.ASCII.GetBytes(_settings.ApiKey + ":x"));

        for (var attempt = 1; ; attempt++)
        {
            using var request = new HttpRequestMessage(method, uri);
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", auth);
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
                throw new ApiException(502, $"The billing system could not be reached: {ex.Message}");
            }

            using (response)
            {
                var text = await response.Content.ReadAsStringAsync(ct);

                if (response.StatusCode == HttpStatusCode.TooManyRequests && attempt < MAX_ATTEMPTS)
                {
                    var delay = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(attempt);
                    await Task.Delay(delay > TimeSpan.FromSeconds(10) ? TimeSpan.FromSeconds(10) : delay, ct);
                    continue;
                }

                if (response.IsSuccessStatusCode)
                {
                    if (string.IsNullOrWhiteSpace(text))
                    {
                        return default;
                    }

                    try
                    {
                        using var doc = JsonDocument.Parse(text);
                        return doc.RootElement.Clone();
                    }
                    catch (JsonException)
                    {
                        return default;
                    }
                }

                throw ToException(response.StatusCode, text);
            }
        }
    }

    private static ApiException ToException(HttpStatusCode status, string text)
    {
        var reason = ExtractReasons(text);
        return status switch
        {
            HttpStatusCode.NotFound => new ApiException(404, string.IsNullOrEmpty(reason) ? "Not found." : reason),
            HttpStatusCode.UnprocessableEntity or HttpStatusCode.BadRequest => new ApiException(422, string.IsNullOrEmpty(reason) ? "The billing system rejected the request." : reason),
            HttpStatusCode.Conflict => new ApiException(409, string.IsNullOrEmpty(reason) ? "Conflict." : reason),
            HttpStatusCode.TooManyRequests => new ApiException(503, "The billing system is busy, try again shortly."),
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => new ApiException(502, "The billing system rejected our credentials."),
            _ => new ApiException(502, $"The billing system failed ({(int)status}).")
        };
    }

    private static string ExtractReasons(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "";
        }

        try
        {
            using var doc = JsonDocument.Parse(text);
            var messages = new List<string>();
            var root = doc.RootElement;
            Collect(root.ValueKind == JsonValueKind.Object && root.TryGetProperty("errors", out var errors) ? errors : root, messages);
            return string.Join(" ", messages.Distinct());
        }
        catch (JsonException)
        {
            return "";
        }
    }

    private static void Collect(JsonElement element, List<string> messages)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                messages.Add(element.GetString()!);
                break;
            case JsonValueKind.Array:
                foreach (var e in element.EnumerateArray()) Collect(e, messages);
                break;
            case JsonValueKind.Object:
                foreach (var p in element.EnumerateObject()) Collect(p.Value, messages);
                break;
        }
    }
}
