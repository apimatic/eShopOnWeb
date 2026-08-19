namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// A single product read from a supplier's product listing. Fields are nullable because a listing
/// may omit some of them; the sync service decides how to sanitize and match each product.
/// </summary>
public record ScrapedProduct
{
    /// <summary>The product's display name.</summary>
    public string? Name { get; init; }

    /// <summary>A description of the product.</summary>
    public string? Description { get; init; }

    /// <summary>The product's price.</summary>
    public decimal? Price { get; init; }

    /// <summary>The product's brand or manufacturer.</summary>
    public string? Brand { get; init; }

    /// <summary>The supplier's own identifier / SKU for the product, when the listing exposes one.</summary>
    public string? Sku { get; init; }

    /// <summary>The absolute URL of the product's own page on the supplier's site, when available.</summary>
    public string? ProductUrl { get; init; }
}
