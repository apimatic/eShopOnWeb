using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Thin HTTP client for the Maxio Advanced Billing API. Built strictly against
/// maxio-spec/openapi.yaml: basic auth (API key as username, "x" as password),
/// JSON bodies/responses with snake_case property names and resource envelopes.
/// </summary>
public sealed class MaxioApiClient
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;

    public MaxioApiClient(HttpClient httpClient, IOptions<MaxioOptions> options)
    {
        var apiKey = options.Value.ApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException(
                "Maxio configuration is incomplete: Maxio:ApiKey is required (source: MAXIO_API_KEY).");
        }

        _httpClient = httpClient;
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes($"{apiKey}:x")));
        if (_httpClient.DefaultRequestHeaders.Accept.Count == 0)
        {
            _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        }
    }

    /// <summary>
    /// GET returning null on 404 (for lookups where "not found" is a normal outcome).
    /// </summary>
    public async Task<T?> GetOptionalAsync<T>(string path, IEnumerable<KeyValuePair<string, string>>? queryParams = null, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync(BuildUri(path, queryParams), cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return default;
        }
        return await ReadAsync<T>(response, cancellationToken);
    }

    /// <summary>POST a JSON request body and deserialize the JSON response.</summary>
    public async Task<T> PostAsync<T>(string path, object requestBody, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PostAsJsonAsync(BuildUri(path, null), requestBody, SerializerOptions, cancellationToken);
        return await ReadAsync<T>(response, cancellationToken)
               ?? throw new MaxioApiException((int)response.StatusCode, string.Empty, Array.Empty<string>());
    }

    private static Uri BuildUri(string path, IEnumerable<KeyValuePair<string, string>>? queryParams)
    {
        if (queryParams == null)
        {
            return new Uri(path, UriKind.Relative);
        }

        var query = string.Join("&", queryParams
            .Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));
        return new Uri($"{path}?{query}", UriKind.Relative);
    }

    private static async Task<T?> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            if (string.IsNullOrWhiteSpace(body))
            {
                return default;
            }
            return JsonSerializer.Deserialize<T>(body, SerializerOptions);
        }

        throw new MaxioApiException((int)response.StatusCode, body, ParseErrors(body));
    }

    /// <summary>
    /// Parses Maxio error bodies per the spec's error schemas, e.g.
    /// {"errors":["First name: cannot be blank."]} or {"errors":{"customer":"can't be blank"}}.
    /// </summary>
    private static IReadOnlyList<string> ParseErrors(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("errors", out var errors))
            {
                if (errors.ValueKind == JsonValueKind.Array)
                {
                    return errors.EnumerateArray()
                        .Select(e => e.ToString())
                        .Where(s => !string.IsNullOrEmpty(s))
                        .Select(s => s!)
                        .ToList();
                }
                if (errors.ValueKind == JsonValueKind.Object)
                {
                    return errors.EnumerateObject()
                        .Select(p => $"{p.Name}: {p.Value}")
                        .ToList();
                }
            }
            if (document.RootElement.ValueKind == JsonValueKind.String)
            {
                return new[] { document.RootElement.GetString() ?? body };
            }
        }
        catch (JsonException)
        {
            // fall through: non-JSON error body (HTML error page etc.)
        }
        return new[] { body };
    }
}
