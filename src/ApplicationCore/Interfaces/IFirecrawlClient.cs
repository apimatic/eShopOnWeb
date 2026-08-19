using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Reads a supplier's product listing page and returns the products found on it.
/// Backed by Firecrawl's structured scrape capability.
/// </summary>
public interface IFirecrawlClient
{
    /// <summary>
    /// Scrapes the given product listing page and extracts every product it can find.
    /// </summary>
    /// <exception cref="FirecrawlException">Thrown when the page cannot be read or the response is unusable.</exception>
    Task<IReadOnlyList<ScrapedProduct>> ScrapeProductsAsync(string listingUrl, CancellationToken cancellationToken = default);
}

/// <summary>
/// A single product as captured from a supplier's listing page. Every field except <see cref="Name"/>
/// may be absent depending on how much the page exposes.
/// </summary>
public record ScrapedProduct
{
    public string? Name { get; init; }
    public string? Description { get; init; }

    /// <summary>Raw price text as it appeared on the page (e.g. "$19.99"), parsed downstream.</summary>
    public string? Price { get; init; }

    public string? Brand { get; init; }

    /// <summary>The supplier's stock-keeping unit / product code, if the page exposes one.</summary>
    public string? Sku { get; init; }

    /// <summary>A link to the product's own page, if the listing exposes one.</summary>
    public string? ProductUrl { get; init; }
}
