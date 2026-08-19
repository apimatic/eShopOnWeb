using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Firecrawl.Models;

namespace Microsoft.eShopWeb.Infrastructure.Firecrawl;

/// <summary>
/// Hand-written Firecrawl API client. The <see cref="HttpClient"/> is configured (base address,
/// bearer auth) by the typed-client registration in composition root; this class only builds the
/// spec's requests and reads the spec's responses.
/// </summary>
public class FirecrawlClient : IFirecrawlClient
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;

    public FirecrawlClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<FirecrawlExtractResponse> StartExtractAsync(FirecrawlExtractRequest request, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.PostAsJsonAsync("extract", request, SerializerOptions, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var result = await response.Content.ReadFromJsonAsync<FirecrawlExtractResponse>(SerializerOptions, cancellationToken);
        if (result is null)
        {
            throw new FirecrawlException("Firecrawl returned an empty response when starting an extract job.");
        }
        return result;
    }

    public async Task<FirecrawlExtractStatusResponse> GetExtractStatusAsync(string jobId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(jobId))
        {
            throw new ArgumentException("A Firecrawl extract job id is required.", nameof(jobId));
        }

        using var response = await _httpClient.GetAsync($"extract/{Uri.EscapeDataString(jobId)}", cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var result = await response.Content.ReadFromJsonAsync<FirecrawlExtractStatusResponse>(SerializerOptions, cancellationToken);
        if (result is null)
        {
            throw new FirecrawlException($"Firecrawl returned an empty status response for extract job {jobId}.");
        }
        return result;
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var statusCode = (int)response.StatusCode;
        string? message = null;
        try
        {
            var error = await response.Content.ReadFromJsonAsync<FirecrawlErrorResponse>(SerializerOptions, cancellationToken);
            message = error?.Error;
        }
        catch
        {
            // Body was not the spec error shape; fall back to the raw content below.
        }

        if (string.IsNullOrWhiteSpace(message))
        {
            message = await response.Content.ReadAsStringAsync(cancellationToken);
        }

        throw new FirecrawlException(
            $"Firecrawl request failed with status {statusCode}: {message}", statusCode);
    }
}
