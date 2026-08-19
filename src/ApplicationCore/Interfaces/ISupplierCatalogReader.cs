using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// A single product read from a supplier's product listing page. All fields are best-effort: the
/// reader fills what it can extract and leaves the rest null for the importer to handle.
/// </summary>
/// <param name="Name">Product name.</param>
/// <param name="Description">Product description.</param>
/// <param name="Price">Product price.</param>
/// <param name="Brand">Product brand.</param>
/// <param name="ProductKey">The supplier's own stable identifier or URL for this product, used to match it against the catalog.</param>
public record SupplierProduct(
    string? Name,
    string? Description,
    decimal? Price,
    string? Brand,
    string? ProductKey);

/// <summary>
/// Reads a supplier's product listing page and returns the products it lists. Implemented over
/// Firecrawl in the Infrastructure layer; the domain depends only on this abstraction.
/// </summary>
public interface ISupplierCatalogReader
{
    Task<IReadOnlyList<SupplierProduct>> ReadProductListingAsync(string listingUrl, CancellationToken cancellationToken = default);
}
