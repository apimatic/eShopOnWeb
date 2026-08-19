using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Reads a supplier's product listing page and returns the products found on it.
/// This is the seam behind which the concrete web-reading technology (Firecrawl) lives; the
/// sync logic depends only on this abstraction.
/// </summary>
public interface ISupplierCatalogReader
{
    Task<SupplierListingReadResult> ReadListingAsync(string listingUrl, CancellationToken cancellationToken = default);
}

/// <summary>
/// The outcome of reading a supplier listing: the products found, plus whether the reader is
/// confident it captured the supplier's <em>whole</em> listing or only part of it.
/// </summary>
public sealed class SupplierListingReadResult
{
    public IReadOnlyList<ScrapedProduct> Products { get; }

    /// <summary>
    /// True when the reader captured the entire listing; false when the read was truncated
    /// (e.g. the reader returned a partial page or paging could not be followed to the end).
    /// </summary>
    public bool ListingFullyCaptured { get; }

    public SupplierListingReadResult(IReadOnlyList<ScrapedProduct> products, bool listingFullyCaptured)
    {
        Products = products;
        ListingFullyCaptured = listingFullyCaptured;
    }
}

/// <summary>
/// A single product as read from a supplier's listing page. Names come off arbitrary web pages,
/// so every field is best-effort and may be missing; the sync layer is responsible for
/// validating and normalizing before importing.
/// </summary>
public sealed class ScrapedProduct
{
    /// <summary>
    /// The supplier's own stable identifier for the product — its product-detail URL, or a SKU/id
    /// if the page exposes one. Used to match the product back to the same catalog item on re-sync.
    /// </summary>
    public string? ExternalId { get; init; }

    public string? Name { get; init; }
    public string? Description { get; init; }
    public decimal? Price { get; init; }
    public string? Brand { get; init; }
}
