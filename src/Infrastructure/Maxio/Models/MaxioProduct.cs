using System;

namespace Microsoft.eShopWeb.Infrastructure.Maxio.Models;

/// <summary>
/// Product as returned by the Maxio API (spec schema "Product"; serialized snake_case).
/// Only the fields this integration consumes are modeled.
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
    public DateTimeOffset? ArchivedAt { get; set; }
    public MaxioProductFamily? ProductFamily { get; set; }
}

/// <summary>
/// Spec schema "Product-Family".
/// </summary>
public class MaxioProductFamily
{
    public long Id { get; set; }
    public string? Name { get; set; }
    public string? Handle { get; set; }
}

/// <summary>
/// Spec schema "Product-Response": wraps a product in a top-level "product" property.
/// </summary>
public class MaxioProductResponse
{
    public MaxioProduct? Product { get; set; }
}
