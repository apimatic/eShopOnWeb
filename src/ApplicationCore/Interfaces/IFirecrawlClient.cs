using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Reads a supplier's product listing page through Firecrawl and returns the products it finds
/// as structured data. This is the only capability the supplier-catalog sync needs from Firecrawl.
/// </summary>
public interface IFirecrawlClient
{
    Task<FirecrawlScrapeResult> ScrapeProductListingAsync(string listingUrl, CancellationToken cancellationToken = default);
}

/// <summary>Outcome of scraping a single supplier listing page.</summary>
public class FirecrawlScrapeResult
{
    /// <summary>True when Firecrawl returned a usable response for the page.</summary>
    public bool Success { get; init; }

    /// <summary>Products extracted from the listing (empty when the page had none or the call failed).</summary>
    public IReadOnlyList<ScrapedProduct> Products { get; init; } = new List<ScrapedProduct>();

    /// <summary>Non-fatal warning surfaced by Firecrawl, if any.</summary>
    public string? Warning { get; init; }

    /// <summary>Populated when <see cref="Success"/> is false: why the page could not be read.</summary>
    public string? Error { get; init; }

    public static FirecrawlScrapeResult Ok(IReadOnlyList<ScrapedProduct> products, string? warning = null) =>
        new() { Success = true, Products = products, Warning = warning };

    public static FirecrawlScrapeResult Fail(string error) =>
        new() { Success = false, Error = error };
}

/// <summary>A single product captured from a supplier's listing.</summary>
public class ScrapedProduct
{
    public string? Name { get; init; }
    public string? Description { get; init; }
    public decimal? Price { get; init; }
    public string? Brand { get; init; }

    /// <summary>The product's own URL on the supplier site, if present.</summary>
    public string? Url { get; init; }

    /// <summary>The supplier's own identifier for the product (SKU / id), if present.</summary>
    public string? ExternalId { get; init; }
}
