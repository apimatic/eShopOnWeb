using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The outcome of reading a supplier's listing: the products found, and whether the whole
/// listing could actually be read. <see cref="ListingFullyCaptured"/> is <c>false</c> when the
/// underlying crawl did not complete every page (e.g. a page failed or a limit was hit), which
/// downgrades the sync to a partial result even if every captured product imports cleanly.
/// </summary>
public class SupplierScrapeResult
{
    public SupplierScrapeResult(IReadOnlyList<ScrapedProduct> products, bool listingFullyCaptured)
    {
        Products = products;
        ListingFullyCaptured = listingFullyCaptured;
    }

    public IReadOnlyList<ScrapedProduct> Products { get; }

    public bool ListingFullyCaptured { get; }
}

/// <summary>
/// A single product as read from a supplier's listing. Field completeness varies by source;
/// the importer decides whether a given product carries enough to become a catalog item.
/// </summary>
public class ScrapedProduct
{
    /// <summary>The supplier's own stable identifier (SKU) for the product, if present.</summary>
    public string? ExternalId { get; set; }

    /// <summary>The supplier's URL for the product, if present. Used as a fallback match key.</summary>
    public string? Url { get; set; }

    public string? Name { get; set; }

    public string? Description { get; set; }

    /// <summary>Numeric price, or <c>null</c> when the listing shows no usable price (e.g. "Contact for pricing").</summary>
    public decimal? Price { get; set; }

    public string? Brand { get; set; }
}
