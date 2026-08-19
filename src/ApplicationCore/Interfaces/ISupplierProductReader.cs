using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Reads a supplier's public product-listing page and returns the products found on it.
/// Implemented in the Infrastructure layer (currently backed by Firecrawl); the domain
/// depends only on this abstraction.
/// </summary>
public interface ISupplierProductReader
{
    Task<SupplierProductReadResult> ReadProductsAsync(string listingUrl, CancellationToken cancellationToken = default);
}

/// <summary>
/// A single product captured from a supplier's listing. Everything except
/// <see cref="ExternalId"/> is best-effort — the reader returns whatever it could capture, and
/// the sync decides what is importable.
/// </summary>
/// <param name="ExternalId">The supplier's own stable identifier for the product (its URL or id). Used for matching on re-sync.</param>
public record SupplierProduct(
    string ExternalId,
    string? Name,
    string? Description,
    decimal? Price,
    string? Brand,
    string? ImageUrl);

/// <summary>
/// The outcome of reading a listing.
/// </summary>
/// <param name="Products">The products captured from the listing.</param>
/// <param name="ListingFullyCaptured">
/// True when the reader is confident it read the supplier's entire listing; false when it could
/// only capture part of it (e.g. the extraction job did not fully complete).
/// </param>
public record SupplierProductReadResult(
    IReadOnlyList<SupplierProduct> Products,
    bool ListingFullyCaptured);
