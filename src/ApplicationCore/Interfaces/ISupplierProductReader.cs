using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Reads a supplier's product listing page and returns the products it advertises.
/// The implementation is responsible for the mechanics of fetching and parsing the page
/// (e.g. via Firecrawl); callers only see the resulting products.
/// </summary>
public interface ISupplierProductReader
{
    Task<SupplierProductReadResult> ReadListingAsync(string listingUrl, CancellationToken cancellationToken = default);
}

/// <summary>Outcome of reading a supplier listing.</summary>
public class SupplierProductReadResult
{
    /// <summary>True when the listing was read to completion; false when the read failed.</summary>
    public bool Succeeded { get; init; }

    /// <summary>The products discovered in the listing. Empty when the read failed.</summary>
    public IReadOnlyList<SupplierProduct> Products { get; init; } = new List<SupplierProduct>();

    /// <summary>Diagnostic detail, primarily populated on failure.</summary>
    public string? Detail { get; init; }

    public static SupplierProductReadResult Success(IReadOnlyList<SupplierProduct> products, string? detail = null) =>
        new() { Succeeded = true, Products = products, Detail = detail };

    public static SupplierProductReadResult Failure(string detail) =>
        new() { Succeeded = false, Detail = detail };
}

/// <summary>A single product as advertised on a supplier's listing page.</summary>
public class SupplierProduct
{
    public string? Name { get; init; }
    public string? Description { get; init; }

    /// <summary>The price exactly as shown on the page (may be non-numeric, e.g. "Contact for pricing").</summary>
    public string? Price { get; init; }
    public string? Brand { get; init; }

    /// <summary>The supplier's own product code / SKU, if shown.</summary>
    public string? Sku { get; init; }

    /// <summary>The product's page URL, if available.</summary>
    public string? Url { get; init; }

    /// <summary>The product category, if shown.</summary>
    public string? Category { get; init; }
}
