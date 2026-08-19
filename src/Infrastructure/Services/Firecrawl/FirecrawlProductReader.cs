using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services.Firecrawl;

/// <summary>
/// Reads a supplier's product listing via Firecrawl's structured extraction (POST /extract,
/// polled through GET /extract/{id}). A JSON Schema instructs Firecrawl to return every product
/// with its name, description, price, brand and the supplier's own identifier, spanning all
/// pages of the listing.
/// </summary>
public class FirecrawlProductReader : ISupplierProductReader
{
    private readonly FirecrawlClient _client;
    private readonly FirecrawlOptions _options;
    private readonly IAppLogger<FirecrawlProductReader> _logger;

    public FirecrawlProductReader(
        FirecrawlClient client,
        IOptions<FirecrawlOptions> options,
        IAppLogger<FirecrawlProductReader> logger)
    {
        _client = client;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<SupplierProductReadResult> ReadListingAsync(string listingUrl, CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(listingUrl, UriKind.Absolute, out var listingUri))
        {
            return SupplierProductReadResult.Failure($"Listing URL '{listingUrl}' is not a valid absolute URL.");
        }

        var request = new ExtractRequest
        {
            Urls = BuildUrls(listingUri),
            Prompt =
                "Extract every product shown in this supplier's product catalog/listing, including products " +
                "on every page of the listing (follow pagination such as 'next page' links). For each product " +
                "capture its name, description, price exactly as shown (keep the currency symbol; if no numeric " +
                "price is shown capture the text shown, e.g. 'Contact for pricing'), brand, the supplier's own " +
                "SKU or product code, the product's URL if present, and its category if shown. Do not skip any product.",
            Schema = BuildSchema(),
            EnableWebSearch = false,
            IncludeSubdomains = false,
            IgnoreInvalidURLs = true
        };

        try
        {
            var start = await _client.StartExtractAsync(request, cancellationToken);
            _logger.LogInformation($"Firecrawl extract started (job {start.Id}) for listing {listingUri}.");

            var status = await PollUntilTerminalAsync(start.Id!, cancellationToken);

            if (!string.Equals(status.Status, "completed", StringComparison.OrdinalIgnoreCase))
            {
                var detail = status.Error ?? $"Firecrawl extract ended with status '{status.Status}'.";
                return SupplierProductReadResult.Failure(detail);
            }

            var products = ParseProducts(status.Data);
            _logger.LogInformation($"Firecrawl extract job {start.Id} returned {products.Count} product(s).");
            return SupplierProductReadResult.Success(products);
        }
        catch (FirecrawlApiException ex)
        {
            _logger.LogWarning($"Firecrawl read failed for {listingUri}: {ex.Message}");
            return SupplierProductReadResult.Failure(ex.Message);
        }
    }

    private async Task<ExtractStatusResponse> PollUntilTerminalAsync(string jobId, CancellationToken cancellationToken)
    {
        var pollInterval = TimeSpan.FromSeconds(Math.Max(1, _options.ExtractPollIntervalSeconds));
        var timeout = TimeSpan.FromSeconds(Math.Max(5, _options.ExtractTimeoutSeconds));
        var stopwatch = Stopwatch.StartNew();

        while (true)
        {
            var status = await _client.GetExtractAsync(jobId, cancellationToken);

            if (IsTerminal(status.Status))
            {
                return status;
            }

            if (stopwatch.Elapsed >= timeout)
            {
                throw new FirecrawlApiException(
                    $"Firecrawl extract job {jobId} did not complete within {timeout.TotalSeconds:0}s (last status '{status.Status}').");
            }

            await Task.Delay(pollInterval, cancellationToken);
        }
    }

    private static bool IsTerminal(string? status) =>
        string.Equals(status, "completed", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(status, "failed", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(status, "cancelled", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Builds the URL set for extraction: the listing page itself plus a site-wide glob so the
    /// whole listing (subsequent pages, product pages) is captured, not just the first page.
    /// </summary>
    private static List<string> BuildUrls(Uri listingUri)
    {
        var siteGlob = $"{listingUri.Scheme}://{listingUri.Authority}/*";
        var urls = new List<string> { listingUri.AbsoluteUri };
        if (!urls.Contains(siteGlob, StringComparer.OrdinalIgnoreCase))
        {
            urls.Add(siteGlob);
        }
        return urls;
    }

    private static object BuildSchema() => new
    {
        type = "object",
        properties = new
        {
            products = new
            {
                type = "array",
                description = "Every product listed in the supplier's catalog/listing, across all pages.",
                items = new
                {
                    type = "object",
                    properties = new
                    {
                        name = new { type = "string", description = "The product name or title." },
                        description = new { type = "string", description = "The product description." },
                        price = new
                        {
                            type = "string",
                            description = "The price exactly as shown, including the currency symbol; if no numeric price is shown, the text shown instead."
                        },
                        brand = new { type = "string", description = "The product brand or manufacturer." },
                        sku = new { type = "string", description = "The supplier's own product code, SKU or identifier, if shown." },
                        url = new { type = "string", description = "The product's own page URL, if available." },
                        category = new { type = "string", description = "The product category, if shown." }
                    },
                    required = new[] { "name", "description", "price", "brand" }
                }
            }
        },
        required = new[] { "products" }
    };

    private List<SupplierProduct> ParseProducts(JsonElement data)
    {
        var extracted = ExtractRows(data);
        return extracted
            .Select(p => new SupplierProduct
            {
                Name = Clean(p.Name),
                Description = Clean(p.Description),
                Price = Clean(p.Price),
                Brand = Clean(p.Brand),
                Sku = Clean(p.Sku),
                Url = Clean(p.Url),
                Category = Clean(p.Category)
            })
            .ToList();
    }

    /// <summary>
    /// Pulls the product rows out of the extraction result, tolerating either
    /// <c>{ "products": [...] }</c> or a bare top-level array.
    /// </summary>
    private List<ExtractedProduct> ExtractRows(JsonElement data)
    {
        try
        {
            if (data.ValueKind == JsonValueKind.Object)
            {
                if (data.TryGetProperty("products", out var productsElement) &&
                    productsElement.ValueKind == JsonValueKind.Array)
                {
                    return productsElement.Deserialize<List<ExtractedProduct>>() ?? new List<ExtractedProduct>();
                }
            }
            else if (data.ValueKind == JsonValueKind.Array)
            {
                return data.Deserialize<List<ExtractedProduct>>() ?? new List<ExtractedProduct>();
            }
        }
        catch (JsonException ex)
        {
            _logger.LogWarning($"Firecrawl extract data could not be parsed into products: {ex.Message}");
        }

        return new List<ExtractedProduct>();
    }

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
