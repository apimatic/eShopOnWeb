using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Reads a supplier's product listing page and returns the products found on it.
/// The concrete implementation performs the actual web read (via Firecrawl) and lives in the
/// Infrastructure layer, keeping the domain free of any integration detail.
/// </summary>
public interface ISupplierListingReader
{
    Task<SupplierListingResult> ReadListingAsync(string listingUrl, CancellationToken cancellationToken = default);
}

/// <summary>The outcome of reading a supplier's listing page.</summary>
public class SupplierListingResult
{
    public SupplierListingResult(IReadOnlyList<ScrapedProduct> products, bool listingFullyCaptured)
    {
        Products = products;
        ListingFullyCaptured = listingFullyCaptured;
    }

    public IReadOnlyList<ScrapedProduct> Products { get; }

    /// <summary>
    /// True when the read is believed to have captured the supplier's whole listing; false when
    /// it is known to have captured only part of it (e.g. the source signalled truncation).
    /// </summary>
    public bool ListingFullyCaptured { get; }
}

/// <summary>A single product as read from a supplier's listing, before it is matched into the catalog.</summary>
public class ScrapedProduct
{
    public string? Name { get; set; }
    public string? Description { get; set; }
    public decimal? Price { get; set; }
    public string? Brand { get; set; }
    public string? Category { get; set; }

    /// <summary>The product's own page URL, if the listing exposes one.</summary>
    public string? Url { get; set; }

    /// <summary>The supplier's own identifier/SKU for the product, if the listing exposes one.</summary>
    public string? Sku { get; set; }
}
