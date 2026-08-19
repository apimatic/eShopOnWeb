using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Outcome of reading a supplier's listing.
/// </summary>
public class SupplierListingResult
{
    /// <summary>The products found across the whole listing.</summary>
    public IReadOnlyList<SupplierProduct> Products { get; }

    /// <summary>
    /// True when the entire listing was read successfully; false when only part of it could be
    /// read (for example some listing pages failed to load), which downgrades the sync outcome.
    /// </summary>
    public bool FullyCaptured { get; }

    /// <summary>Optional note about the read (e.g. which pages failed).</summary>
    public string? Detail { get; }

    public SupplierListingResult(IReadOnlyList<SupplierProduct> products, bool fullyCaptured, string? detail = null)
    {
        Products = products;
        FullyCaptured = fullyCaptured;
        Detail = detail;
    }
}

/// <summary>
/// A single product as advertised on a supplier's listing.
/// </summary>
public class SupplierProduct
{
    /// <summary>The supplier's own identifier (SKU) or listing URL for the product; used to match on re-sync.</summary>
    public string? ExternalId { get; init; }

    public string? Name { get; init; }
    public string? Description { get; init; }
    public string? Brand { get; init; }

    /// <summary>Numeric price, or null when the listing shows no purchasable price (e.g. "Contact for pricing").</summary>
    public decimal? Price { get; init; }
}
