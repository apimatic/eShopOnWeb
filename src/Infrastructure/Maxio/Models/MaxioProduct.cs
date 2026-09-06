using System;

namespace Microsoft.eShopWeb.Infrastructure.Maxio.Models;

/// <summary>
/// Mirrors the <c>Product</c> schema of the Maxio OpenAPI specification. Only the fields this
/// integration reads are modelled; unknown members are ignored so the provider can extend its
/// payloads without breaking us.
/// </summary>
public class MaxioProduct
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public string? Handle { get; set; }
    public string? Description { get; set; }
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string? IntervalUnit { get; set; }
    public bool RequireCreditCard { get; set; }
    public long? TrialPriceInCents { get; set; }
    public int? TrialInterval { get; set; }
    public string? TrialIntervalUnit { get; set; }
    public bool Taxable { get; set; }
    public DateTimeOffset? ArchivedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public int? ProductPricePointId { get; set; }
    public string? ProductPricePointName { get; set; }
    public MaxioProductFamily? ProductFamily { get; set; }
}

/// <summary>Mirrors the <c>Product-Family</c> schema.</summary>
public class MaxioProductFamily
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public string? Handle { get; set; }
    public string? Description { get; set; }
}

/// <summary>Mirrors the <c>Product-Response</c> wrapper: <c>{ "product": { ... } }</c>.</summary>
public class MaxioProductEnvelope
{
    public MaxioProduct? Product { get; set; }
}
