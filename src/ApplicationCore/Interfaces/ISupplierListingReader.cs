using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Reads a supplier's product listing page (which may span several pages) and returns the
/// products it advertises. Implemented on top of a web-reading provider (Firecrawl) so the
/// rest of the application does not depend on how the listing is fetched.
/// </summary>
public interface ISupplierListingReader
{
    Task<SupplierListingResult> ReadListingAsync(string listingUrl, CancellationToken cancellationToken = default);
}
