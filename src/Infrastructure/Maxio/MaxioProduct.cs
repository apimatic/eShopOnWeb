using System;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// A Maxio product (a plan). Mirrors the <c>Product.yaml</c> schema from the Maxio OpenAPI spec.
/// </summary>
public class MaxioProduct
{
    public long Id { get; set; }

    public string? Name { get; set; }

    public string? Handle { get; set; }

    public string? Description { get; set; }

    public long? PriceInCents { get; set; }

    public int? Interval { get; set; }

    public string? IntervalUnit { get; set; }

    public bool? RequireCreditCard { get; set; }

    public bool? Taxable { get; set; }

    public DateTimeOffset? ArchivedAt { get; set; }

    public string? ProductPricePointName { get; set; }

    public MaxioProductFamily? ProductFamily { get; set; }
}
