using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Reads a supplier's product listing page (which may span multiple pages) and returns the
/// products found on it. The concrete implementation lives in Infrastructure and is the only
/// place that talks to the external scraping provider.
/// </summary>
public interface ISupplierCatalogScraper
{
    /// <summary>
    /// Captures every product reachable from <paramref name="listingUrl"/>.
    /// </summary>
    Task<SupplierScrapeResult> ScrapeListingAsync(string listingUrl, CancellationToken cancellationToken = default);
}
