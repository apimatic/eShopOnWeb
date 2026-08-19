using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Firecrawl;

/// <summary>
/// Reads a supplier's product listing by crawling it with Firecrawl and asking Firecrawl's
/// <c>json</c> output format to extract structured products from each page. The crawl follows
/// the listing's own pagination, so multi-page listings are captured in full.
/// </summary>
internal sealed class FirecrawlSupplierListingReader : ISupplierListingReader
{
    private const string ExtractionPrompt =
        "This is a supplier's product listing page. Extract every product advertised on this page. " +
        "For each product capture its full name, its description, its brand, its SKU or product code, " +
        "and its price. If a product shows no numeric price (for example 'Contact for pricing'), set " +
        "price to null. If the page shows no products at all, return an empty products array. " +
        "Do not invent products that are not on the page.";

    private readonly IFirecrawlClient _client;
    private readonly FirecrawlOptions _options;
    private readonly IAppLogger<FirecrawlSupplierListingReader> _logger;

    public FirecrawlSupplierListingReader(
        IFirecrawlClient client,
        IOptions<FirecrawlOptions> options,
        IAppLogger<FirecrawlSupplierListingReader> logger)
    {
        _client = client;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<SupplierListingResult> ReadListingAsync(string listingUrl, CancellationToken cancellationToken = default)
    {
        var request = new CrawlRequest
        {
            Url = listingUrl,
            Limit = _options.MaxPages,
            CrawlEntireDomain = true,
            Sitemap = "include",
            ScrapeOptions = new CrawlScrapeOptions
            {
                OnlyMainContent = true,
                Formats = new List<object> { new JsonFormat { Prompt = ExtractionPrompt, Schema = BuildSchema() } }
            }
        };

        var crawl = await _client.CrawlAsync(request, cancellationToken);

        var products = new List<SupplierProduct>();
        int pagesWithError = 0;
        int pagesWithoutExtraction = 0;

        foreach (var page in crawl.Data)
        {
            if (!string.IsNullOrWhiteSpace(page.Metadata?.Error))
            {
                pagesWithError++;
                continue;
            }

            if (page.Json is null)
            {
                pagesWithoutExtraction++;
                continue;
            }

            var source = page.Metadata?.SourceUrl ?? page.Metadata?.Url;
            products.AddRange(MapProducts(page.Json.Value, source));
        }

        bool completedStatus = string.Equals(crawl.Status, "completed", StringComparison.OrdinalIgnoreCase);
        bool allPagesCovered = crawl.Completed >= crawl.Total && crawl.Total > 0;
        bool fullyCaptured = completedStatus && allPagesCovered && pagesWithError == 0 && pagesWithoutExtraction == 0;

        // A crawl that ended without a terminal 'completed' status and produced nothing usable is a
        // hard failure, not a partial capture - surface it so the sync is marked Failed.
        if (products.Count == 0 && !completedStatus)
        {
            throw new FirecrawlApiException(HttpStatusCode.BadGateway, "CRAWL_INCOMPLETE",
                $"Firecrawl crawl ended with status '{crawl.Status}' and returned no products.");
        }

        var detail = BuildDetail(crawl, pagesWithError, pagesWithoutExtraction, fullyCaptured);
        _logger.LogInformation(
            $"Firecrawl listing read for {listingUrl}: status={crawl.Status}, pages={crawl.Completed}/{crawl.Total}, " +
            $"products={products.Count}, fullyCaptured={fullyCaptured}.");

        return new SupplierListingResult(products, fullyCaptured, detail);
    }

    private IEnumerable<SupplierProduct> MapProducts(JsonElement json, string? sourceUrl)
    {
        if (json.ValueKind != JsonValueKind.Object
            || !json.TryGetProperty("products", out var productsElement)
            || productsElement.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var element in productsElement.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var name = ReadString(element, "name");
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var sku = ReadString(element, "sku");
            var url = ReadString(element, "url");
            var externalId = FirstNonEmpty(sku, url, sourceUrl is null ? name : $"{sourceUrl}#{name}");

            yield return new SupplierProduct
            {
                Name = name,
                Description = ReadString(element, "description"),
                Brand = ReadString(element, "brand"),
                ExternalId = externalId,
                Price = ReadPrice(element, "price")
            };
        }
    }

    private static object BuildSchema()
    {
        // JSON Schema for the extraction. Built as dictionaries so the exact keys are preserved.
        object StringProp() => new Dictionary<string, object> { ["type"] = new[] { "string", "null" } };

        var productProperties = new Dictionary<string, object>
        {
            ["name"] = new Dictionary<string, object> { ["type"] = "string" },
            ["description"] = StringProp(),
            ["brand"] = StringProp(),
            ["sku"] = StringProp(),
            ["url"] = StringProp(),
            ["price"] = new Dictionary<string, object> { ["type"] = new[] { "number", "null" } }
        };

        var productItem = new Dictionary<string, object>
        {
            ["type"] = "object",
            ["properties"] = productProperties,
            ["required"] = new[] { "name" }
        };

        return new Dictionary<string, object>
        {
            ["type"] = "object",
            ["properties"] = new Dictionary<string, object>
            {
                ["products"] = new Dictionary<string, object>
                {
                    ["type"] = "array",
                    ["items"] = productItem
                }
            },
            ["required"] = new[] { "products" }
        };
    }

    private static string BuildDetail(FirecrawlCrawlResult crawl, int pagesWithError, int pagesWithoutExtraction, bool fullyCaptured)
    {
        if (fullyCaptured)
        {
            return $"Read {crawl.Completed} listing page(s).";
        }

        var parts = new List<string> { $"crawl status '{crawl.Status}', {crawl.Completed}/{crawl.Total} pages" };
        if (pagesWithError > 0)
        {
            parts.Add($"{pagesWithError} page(s) failed to load");
        }
        if (pagesWithoutExtraction > 0)
        {
            parts.Add($"{pagesWithoutExtraction} page(s) yielded no extraction");
        }
        return "Listing not fully captured: " + string.Join(", ", parts) + ".";
    }

    private static string? ReadString(JsonElement element, string name)
    {
        if (element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
        {
            var text = value.GetString();
            return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        }
        return null;
    }

    private static decimal? ReadPrice(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value))
        {
            return null;
        }

        switch (value.ValueKind)
        {
            case JsonValueKind.Number when value.TryGetDecimal(out var number):
                return number;
            case JsonValueKind.String:
                var text = value.GetString();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    var cleaned = new string(text.Where(c => char.IsDigit(c) || c == '.' || c == '-').ToArray());
                    if (decimal.TryParse(cleaned, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
                    {
                        return parsed;
                    }
                }
                return null;
            default:
                return null;
        }
    }

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim() ?? string.Empty;
}
