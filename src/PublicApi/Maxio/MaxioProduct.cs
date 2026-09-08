using System;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>Maxio Product Family (components/schemas/Product-Family.yaml).</summary>
public sealed class MaxioProductFamily
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Handle { get; set; }
}

/// <summary>
/// A Maxio Product (components/schemas/Product.yaml). Products are the "plans" that
/// shoppers subscribe to in this integration.
/// </summary>
public sealed class MaxioProduct
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Handle { get; set; }
    public string? Description { get; set; }

    /// <summary>Price of the product's default price point, in integer cents.</summary>
    public long PriceInCents { get; set; }

    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
    public DateTimeOffset? ArchivedAt { get; set; }

    /// <summary>When true a payment profile is required before a subscription can be created.</summary>
    public bool RequireCreditCard { get; set; }

    public bool Taxable { get; set; }
    public MaxioProductFamily? ProductFamily { get; set; }
}

/// <summary>List/read product responses wrap the product under a "product" key.</summary>
public sealed class MaxioProductResponse
{
    public MaxioProduct Product { get; set; } = new();
}
