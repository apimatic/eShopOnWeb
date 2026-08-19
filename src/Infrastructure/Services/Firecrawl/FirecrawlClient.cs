using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Firecrawl;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services.Firecrawl;

/// <summary>
/// Hand-written client for the Firecrawl v2 API, built directly to the OpenAPI contract in
/// <c>firecrawl-spec/</c>. Uses the spec's bearer-token auth scheme and the <c>/extract</c>
/// endpoints for structured, schema-guided extraction of a supplier's product listing.
/// </summary>
public class FirecrawlClient : IFirecrawlClient
{
    private static readonly JsonSerializerOptions s_serialize = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly JsonSerializerOptions s_deserialize = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly FirecrawlOptions _options;

    public FirecrawlClient(HttpClient httpClient, IOptions<FirecrawlOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    public async Task<FirecrawlExtractJob> StartExtractAsync(
        FirecrawlExtractRequest request, CancellationToken cancellationToken = default)
    {
        var body = new Dictionary<string, object?>
        {
            ["urls"] = request.Urls,
            ["prompt"] = request.Prompt,
            ["schema"] = request.Schema
        };

        using var httpRequest = CreateRequest(HttpMethod.Post, "extract");
        httpRequest.Content = JsonContent.Create(body, options: s_serialize);

        using var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
        var wire = await ReadAsync<StartExtractWire>(response, cancellationToken);

        return new FirecrawlExtractJob
        {
            Success = wire.Success,
            Id = wire.Id,
            InvalidUrls = wire.InvalidUrls
        };
    }

    public async Task<FirecrawlExtractResult> GetExtractStatusAsync(
        string jobId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(jobId))
        {
            throw new ArgumentException("jobId is required.", nameof(jobId));
        }

        using var httpRequest = CreateRequest(HttpMethod.Get, $"extract/{Uri.EscapeDataString(jobId)}");
        using var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
        var wire = await ReadAsync<ExtractStatusWire>(response, cancellationToken);

        return new FirecrawlExtractResult
        {
            Success = wire.Success,
            Status = ParseStatus(wire.Status),
            Data = wire.Data,
            TokensUsed = wire.TokensUsed
        };
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            throw new FirecrawlException(
                "Firecrawl API key is not configured. Set 'Firecrawl:ApiKey' (from FIRECRAWL_API_KEY).");
        }

        var request = new HttpRequestMessage(method, CombineUrl(_options.ResolvedBaseUrl, relativePath));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return request;
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new FirecrawlException(
                $"Firecrawl request failed with status {(int)response.StatusCode} ({response.StatusCode}): {Truncate(payload)}");
        }

        try
        {
            var result = JsonSerializer.Deserialize<T>(payload, s_deserialize);
            if (result is null)
            {
                throw new FirecrawlException("Firecrawl returned an empty response body.");
            }
            return result;
        }
        catch (JsonException ex)
        {
            throw new FirecrawlException($"Could not parse Firecrawl response: {Truncate(payload)}", ex);
        }
    }

    private static FirecrawlJobStatus ParseStatus(string? status) => status?.ToLowerInvariant() switch
    {
        "completed" => FirecrawlJobStatus.Completed,
        "failed" => FirecrawlJobStatus.Failed,
        "cancelled" => FirecrawlJobStatus.Cancelled,
        _ => FirecrawlJobStatus.Processing
    };

    private static string CombineUrl(string baseUrl, string relativePath)
        => $"{baseUrl.TrimEnd('/')}/{relativePath.TrimStart('/')}";

    private static string Truncate(string value)
        => value.Length <= 500 ? value : value.Substring(0, 500) + "...";

    private sealed class StartExtractWire
    {
        [JsonPropertyName("success")]
        public bool Success { get; set; }

        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("invalidURLs")]
        public List<string>? InvalidUrls { get; set; }
    }

    private sealed class ExtractStatusWire
    {
        [JsonPropertyName("success")]
        public bool Success { get; set; }

        [JsonPropertyName("status")]
        public string? Status { get; set; }

        [JsonPropertyName("data")]
        public JsonElement? Data { get; set; }

        [JsonPropertyName("tokensUsed")]
        public int? TokensUsed { get; set; }
    }
}
