using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Reads a supplier's product listing page and returns the products it advertises.
/// The concrete implementation is a web-scraping adapter (Firecrawl); the rest of the
/// application depends only on this contract, never on the scraping technology.
/// </summary>
public interface ISupplierProductScraper
{
    /// <summary>
    /// Captures every product on the supplier's listing at <paramref name="listingUrl"/>.
    /// Throws if the listing cannot be read (the caller treats that as a failed sync).
    /// </summary>
    Task<SupplierScrapeResult> ScrapeListingAsync(string listingUrl, CancellationToken cancellationToken);
}

/// <summary>A single product captured from a supplier's listing.</summary>
/// <param name="ExternalId">
/// The supplier's own stable identifier or URL for the product. Used as the upsert key so
/// re-syncing never duplicates a product already imported.
/// </param>
public record ScrapedProduct(
    string ExternalId,
    string? Name,
    string? Description,
    decimal? Price,
    string? Brand);

/// <summary>The full set of products captured from one listing.</summary>
public record SupplierScrapeResult(IReadOnlyList<ScrapedProduct> Products);
