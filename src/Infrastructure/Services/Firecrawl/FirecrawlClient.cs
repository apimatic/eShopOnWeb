using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services.Firecrawl;

/// <summary>
/// Thin, hand-written client for the Firecrawl v2 API, built directly against the OpenAPI
/// specification in <c>firecrawl-spec/openapi.json</c>. Covers the extract endpoints used to
/// pull structured product data out of a supplier's listing page:
/// <c>POST /extract</c> and <c>GET /extract/{id}</c>. Authentication is the spec's
/// <c>bearerAuth</c> HTTP bearer scheme.
/// </summary>
public class FirecrawlClient
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;

    public FirecrawlClient(HttpClient httpClient, IOptions<FirecrawlOptions> options)
    {
        _httpClient = httpClient;

        var opts = options.Value;
        if (string.IsNullOrWhiteSpace(opts.ApiKey))
        {
            throw new InvalidOperationException(
                "Firecrawl API key is not configured. Set 'Firecrawl:ApiKey' (from the FIRECRAWL_API_KEY environment variable) via user-secrets or configuration.");
        }

        // Base address honors the optional Firecrawl:BaseUrl override, else the spec's server URL.
        var baseAddress = opts.EffectiveBaseUrl.TrimEnd('/') + "/";
        _httpClient.BaseAddress = new Uri(baseAddress);
        _httpClient.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", opts.ApiKey);
    }

    /// <summary>Starts an extract job. Returns the job id to poll.</summary>
    internal async Task<ExtractStartResponse> StartExtractAsync(ExtractRequest request, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.PostAsJsonAsync("extract", request, SerializerOptions, cancellationToken);
        var body = await ReadAsync<ExtractStartResponse>(response, cancellationToken);

        if (!response.IsSuccessStatusCode || body is null || !body.Success || string.IsNullOrEmpty(body.Id))
        {
            var detail = body?.Error ?? $"HTTP {(int)response.StatusCode}";
            throw new FirecrawlApiException($"Firecrawl extract could not be started: {detail}");
        }

        return body;
    }

    /// <summary>Fetches the current state of an extract job.</summary>
    internal async Task<ExtractStatusResponse> GetExtractAsync(string jobId, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync($"extract/{jobId}", cancellationToken);
        var body = await ReadAsync<ExtractStatusResponse>(response, cancellationToken);

        if (!response.IsSuccessStatusCode || body is null)
        {
            var detail = body?.Error ?? $"HTTP {(int)response.StatusCode}";
            throw new FirecrawlApiException($"Firecrawl extract status could not be read: {detail}");
        }

        return body;
    }

    private static async Task<T?> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(content))
        {
            return default;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(content, SerializerOptions);
        }
        catch (JsonException ex)
        {
            throw new FirecrawlApiException(
                $"Firecrawl returned an unparseable response (HTTP {(int)response.StatusCode}): {ex.Message}");
        }
    }
}

/// <summary>Raised when a Firecrawl API call fails or returns an unexpected response.</summary>
public class FirecrawlApiException : Exception
{
    public FirecrawlApiException(string message) : base(message) { }
    public FirecrawlApiException(string message, Exception inner) : base(message, inner) { }
}
