using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Reads a supplier's product listing page and returns the products found on it.
/// The concrete implementation is an infrastructure concern (e.g. Firecrawl); the domain
/// depends only on this abstraction.
/// </summary>
public interface ISupplierListingReader
{
    Task<SupplierListingResult> ReadListingAsync(string listingUrl, CancellationToken cancellationToken = default);
}

/// <summary>
/// The outcome of reading a supplier's listing. <see cref="Success"/> distinguishes "the
/// listing could not be read at all" (a failed sync) from "the listing was read" — even if it
/// contained zero products.
/// </summary>
public class SupplierListingResult
{
    public bool Success { get; }
    public string? Error { get; }
    public IReadOnlyList<SupplierProduct> Products { get; }

    private SupplierListingResult(bool success, string? error, IReadOnlyList<SupplierProduct> products)
    {
        Success = success;
        Error = error;
        Products = products;
    }

    public static SupplierListingResult Ok(IReadOnlyList<SupplierProduct> products) =>
        new(true, null, products);

    public static SupplierListingResult Failed(string error) =>
        new(false, error, new List<SupplierProduct>());
}

/// <summary>
/// A single product captured from a supplier's listing. Name, description, price and brand are
/// the catalog fields; <see cref="Url"/> / <see cref="Sku"/> are the supplier's own identifier
/// for the product, used to match it against the catalog on re-sync.
/// </summary>
public class SupplierProduct
{
    public string? Name { get; set; }
    public string? Description { get; set; }
    public decimal? Price { get; set; }
    public string? Brand { get; set; }

    /// <summary>The supplier's detail URL for this product, if the listing exposes one.</summary>
    public string? Url { get; set; }

    /// <summary>The supplier's SKU / product code for this product, if the listing exposes one.</summary>
    public string? Sku { get; set; }
}
