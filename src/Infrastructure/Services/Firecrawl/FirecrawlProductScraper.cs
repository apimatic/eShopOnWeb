using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Infrastructure.Services.Firecrawl;

/// <summary>
/// <see cref="IFirecrawlProductScraper"/> implemented against the Firecrawl v2 <c>/extract</c>
/// endpoints. The endpoint, request/response shapes, auth scheme and error models all come from
/// the Firecrawl OpenAPI spec; the async extract job maps naturally onto our background sync.
/// </summary>
public class FirecrawlProductScraper : IFirecrawlProductScraper
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;

    public FirecrawlProductScraper(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<string> StartExtractionAsync(string listingUrl, CancellationToken cancellationToken = default)
    {
        var request = new ExtractRequest
        {
            Urls = BuildExtractionUrls(listingUrl),
            Prompt =
                "Extract every product listed across all pages of this supplier's product listing; " +
                "follow pagination links to reach every page. For each product capture: " +
                "externalId (the product's SKU or product id if shown, otherwise the absolute URL of the product), " +
                "name, description, price (a numeric value only — omit the price entirely when there is no " +
                "numeric price, e.g. 'Contact for pricing'), and brand.",
            Schema = BuildProductSchema()
        };

        using var response = await _httpClient.PostAsJsonAsync("extract", request, SerializerOptions, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new FirecrawlApiException(
                $"Firecrawl POST /extract returned {(int)response.StatusCode}: {Summarize(body)}");
        }

        var parsed = Deserialize<ExtractStartResponse>(body);
        if (parsed is null || string.IsNullOrWhiteSpace(parsed.Id))
        {
            throw new FirecrawlApiException(
                $"Firecrawl POST /extract did not return a job id: {Summarize(body)}");
        }

        return parsed.Id!;
    }

    public async Task<ProductExtractionResult> GetExtractionAsync(string jobId, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync($"extract/{Uri.EscapeDataString(jobId)}", cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new FirecrawlApiException(
                $"Firecrawl GET /extract/{jobId} returned {(int)response.StatusCode}: {Summarize(body)}");
        }

        var parsed = Deserialize<ExtractStatusResponse>(body);
        if (parsed is null)
        {
            throw new FirecrawlApiException($"Firecrawl GET /extract/{jobId} returned an unreadable payload.");
        }

        var state = ParseState(parsed.Status, parsed.Error);

        // `data` is only the extracted object once the job is completed; while processing it is an
        // empty array, so only interpret it when it is actually an object.
        ExtractData? data = parsed.Data.ValueKind == JsonValueKind.Object
            ? parsed.Data.Deserialize<ExtractData>(SerializerOptions)
            : null;

        var products = MapProducts(data);
        return new ProductExtractionResult(state, products, parsed.Error);
    }

    private static ExtractionState ParseState(string? status, string? error)
    {
        return status?.Trim().ToLowerInvariant() switch
        {
            "completed" => ExtractionState.Completed,
            "processing" => ExtractionState.Processing,
            "failed" => ExtractionState.Failed,
            "cancelled" => ExtractionState.Cancelled,
            // No recognised status: treat an accompanying error as failure, otherwise keep polling.
            _ => string.IsNullOrWhiteSpace(error) ? ExtractionState.Processing : ExtractionState.Failed
        };
    }

    private static IReadOnlyList<ScrapedProduct> MapProducts(ExtractData? data)
    {
        if (data?.Products is not { Count: > 0 } products)
        {
            return Array.Empty<ScrapedProduct>();
        }

        var mapped = new List<ScrapedProduct>(products.Count);
        foreach (var p in products)
        {
            mapped.Add(new ScrapedProduct(p.ExternalId, p.Name, p.Description, p.Price, p.Brand));
        }

        return mapped;
    }

    /// <summary>
    /// Produces the URLs to extract from: the listing page itself plus a directory-scoped glob so
    /// Firecrawl also reads paginated sibling pages of the same listing. Falls back to the raw URL
    /// if it cannot be parsed.
    /// </summary>
    private static string[] BuildExtractionUrls(string listingUrl)
    {
        if (!Uri.TryCreate(listingUrl, UriKind.Absolute, out var uri))
        {
            return new[] { listingUrl };
        }

        var path = uri.AbsolutePath;
        var lastSlash = path.LastIndexOf('/');
        var directory = lastSlash >= 0 ? path.Substring(0, lastSlash + 1) : "/";
        var glob = $"{uri.Scheme}://{uri.Authority}{directory}*";

        return string.Equals(glob, listingUrl, StringComparison.OrdinalIgnoreCase)
            ? new[] { listingUrl }
            : new[] { listingUrl, glob };
    }

    private static object BuildProductSchema() => new
    {
        type = "object",
        properties = new
        {
            products = new
            {
                type = "array",
                description = "Every product found across all pages of the listing.",
                items = new
                {
                    type = "object",
                    properties = new
                    {
                        externalId = new { type = "string", description = "The supplier's stable id for the product (SKU/product id) or its URL." },
                        name = new { type = "string" },
                        description = new { type = "string" },
                        price = new { type = "number" },
                        brand = new { type = "string" }
                    },
                    required = new[] { "name" }
                }
            }
        },
        required = new[] { "products" }
    };

    private static T? Deserialize<T>(string body)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(body, SerializerOptions);
        }
        catch (JsonException ex)
        {
            throw new FirecrawlApiException($"Could not parse Firecrawl response: {ex.Message}");
        }
    }

    private static string Summarize(string body)
        => string.IsNullOrWhiteSpace(body) ? "(empty response)"
           : body.Length <= 500 ? body
           : body.Substring(0, 500) + "…";
}
