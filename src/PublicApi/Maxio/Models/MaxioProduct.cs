using System;

namespace Microsoft.eShopWeb.PublicApi.Maxio.Models;

/// <summary>
/// A Maxio Product (a subscription plan). Mirrors the subset of Product.yaml used by the
/// integration. The Maxio OpenAPI contract is authoritative for the member names.
/// </summary>
public class MaxioProduct
{
    public long Id { get; set; }

    public string? Name { get; set; }

    public string? Handle { get; set; }

    public string? Description { get; set; }

    public long PriceInCents { get; set; }

    public int Interval { get; set; }

    public string? IntervalUnit { get; set; }

    public bool? RequireCreditCard { get; set; }

    public DateTimeOffset? ArchivedAt { get; set; }

    public MaxioProductFamily? ProductFamily { get; set; }
}
