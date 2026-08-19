using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Reads a supplier's product listing page and returns the products found on it. Abstracts away how
/// the page is actually fetched and parsed (Firecrawl, in this app), keeping the import logic free
/// of any provider detail.
/// </summary>
public interface IProductListingReader
{
    Task<ProductListingReadResult> ReadAsync(string listingUrl, CancellationToken cancellationToken = default);
}

/// <summary>A single product read off a supplier's listing, before it is matched into the catalog.</summary>
public sealed class ScrapedProduct
{
    /// <summary>The supplier's own identifier for the product (SKU) when the listing provides one.</summary>
    public string? Sku { get; init; }

    /// <summary>The absolute URL of the product's own page when the listing provides one.</summary>
    public string? ProductUrl { get; init; }

    public string? Name { get; init; }
    public string? Description { get; init; }
    public decimal? Price { get; init; }
    public string? Currency { get; init; }
    public string? Brand { get; init; }
}

/// <summary>The outcome of reading a supplier's listing.</summary>
public sealed class ProductListingReadResult
{
    /// <summary>Every product found on the listing.</summary>
    public IReadOnlyList<ScrapedProduct> Products { get; }

    /// <summary>The id of the underlying provider job, kept for traceability. May be null.</summary>
    public string? ExternalJobId { get; }

    public ProductListingReadResult(IReadOnlyList<ScrapedProduct> products, string? externalJobId = null)
    {
        Products = products;
        ExternalJobId = externalJobId;
    }
}
