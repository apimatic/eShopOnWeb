using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Firecrawl.Models;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Firecrawl;

/// <summary>
/// Reads a supplier's product listing page with Firecrawl's structured-extract endpoint. The JSON
/// Schema below tells Firecrawl exactly which fields to capture for each product; the request and
/// response shapes come straight from the Firecrawl OpenAPI spec.
/// </summary>
public class FirecrawlProductListingReader : IProductListingReader
{
    private const string ExtractionPrompt =
        "This is a supplier's product listing page. Extract every distinct product shown on it. " +
        "For each product capture its name, full description, numeric price, brand, the currency, " +
        "the absolute URL to the product's own page when present, and its SKU or product identifier " +
        "when present. If a product has no numeric price (for example 'Contact for pricing'), still " +
        "include the product but leave its price empty.";

    // JSON Schema (json-schema.org) passed to Firecrawl's extract endpoint via the request's `schema`.
    private static readonly object ProductsSchema = new
    {
        type = "object",
        properties = new
        {
            products = new
            {
                type = "array",
                items = new
                {
                    type = "object",
                    properties = new
                    {
                        name = new { type = "string" },
                        description = new { type = "string" },
                        price = new { type = "number" },
                        currency = new { type = "string" },
                        brand = new { type = "string" },
                        url = new { type = "string" },
                        sku = new { type = "string" }
                    },
                    required = new[] { "name" }
                }
            }
        },
        required = new[] { "products" }
    };

    private readonly IFirecrawlClient _client;
    private readonly FirecrawlOptions _options;
    private readonly IAppLogger<FirecrawlProductListingReader> _logger;

    public FirecrawlProductListingReader(
        IFirecrawlClient client,
        IOptions<FirecrawlOptions> options,
        IAppLogger<FirecrawlProductListingReader> logger)
    {
        _client = client;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<ProductListingReadResult> ReadAsync(string listingUrl, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            throw new FirecrawlException(
                "No Firecrawl API key is configured. Set 'Firecrawl:ApiKey' (from the FIRECRAWL_API_KEY environment variable) in user-secrets.");
        }

        var request = new FirecrawlExtractRequest
        {
            Urls = new[] { listingUrl },
            Prompt = ExtractionPrompt,
            Schema = ProductsSchema
        };

        var started = await _client.StartExtractAsync(request, cancellationToken);
        if (!started.Success || string.IsNullOrWhiteSpace(started.Id))
        {
            throw new FirecrawlException($"Firecrawl did not start an extract job for '{listingUrl}'.");
        }

        var status = await PollUntilDoneAsync(started.Id!, cancellationToken);

        var products = ParseProducts(status.Data);
        _logger.LogInformation("Firecrawl extract job {0} read {1} product(s) from {2}.",
            started.Id, products.Count, listingUrl);

        return new ProductListingReadResult(products, started.Id);
    }

    private async Task<FirecrawlExtractStatusResponse> PollUntilDoneAsync(string jobId, CancellationToken cancellationToken)
    {
        var pollInterval = TimeSpan.FromSeconds(Math.Max(1, _options.PollIntervalSeconds));
        var timeout = TimeSpan.FromSeconds(Math.Max(pollInterval.TotalSeconds, _options.PollTimeoutSeconds));
        var stopwatch = Stopwatch.StartNew();

        while (true)
        {
            var status = await _client.GetExtractStatusAsync(jobId, cancellationToken);

            switch (status.Status)
            {
                case "completed":
                    return status;
                case "failed":
                case "cancelled":
                    throw new FirecrawlException($"Firecrawl extract job {jobId} ended with status '{status.Status}'.");
            }

            if (stopwatch.Elapsed >= timeout)
            {
                throw new FirecrawlException(
                    $"Firecrawl extract job {jobId} did not complete within {timeout.TotalSeconds:N0}s (last status '{status.Status}').");
            }

            await Task.Delay(pollInterval, cancellationToken);
        }
    }

    private static List<ScrapedProduct> ParseProducts(JsonElement? data)
    {
        var products = new List<ScrapedProduct>();

        if (data is not JsonElement root || root.ValueKind != JsonValueKind.Object)
        {
            return products;
        }

        if (!root.TryGetProperty("products", out var productsElement) || productsElement.ValueKind != JsonValueKind.Array)
        {
            return products;
        }

        foreach (var item in productsElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            products.Add(new ScrapedProduct
            {
                Name = ReadString(item, "name"),
                Description = ReadString(item, "description"),
                Price = ReadDecimal(item, "price"),
                Currency = ReadString(item, "currency"),
                Brand = ReadString(item, "brand"),
                ProductUrl = ReadString(item, "url"),
                Sku = ReadString(item, "sku")
            });
        }

        return products;
    }

    private static string? ReadString(JsonElement item, string property)
    {
        if (!item.TryGetProperty(property, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.ToString(),
            _ => null
        };
    }

    private static decimal? ReadDecimal(JsonElement item, string property)
    {
        if (!item.TryGetProperty(property, out var value))
        {
            return null;
        }

        switch (value.ValueKind)
        {
            case JsonValueKind.Number when value.TryGetDecimal(out var number):
                return number;
            case JsonValueKind.String when decimal.TryParse(
                value.GetString(),
                System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture,
                out var parsed):
                return parsed;
            default:
                return null;
        }
    }
}
