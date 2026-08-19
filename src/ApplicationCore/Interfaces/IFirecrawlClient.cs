using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Reads a supplier's product listing page using Firecrawl and returns the products found on it.
/// </summary>
public interface IFirecrawlClient
{
    /// <summary>
    /// Reads the product listing at <paramref name="listingUrl"/> and returns every product found.
    /// Throws <see cref="Exceptions.FirecrawlException"/> if the listing cannot be read.
    /// </summary>
    Task<IReadOnlyList<ScrapedProduct>> ScrapeProductListingAsync(string listingUrl, CancellationToken cancellationToken = default);
}
