using System;

namespace Microsoft.eShopWeb.Infrastructure.Maxio.Models;

/// <summary>
/// Wire model for a Maxio <c>product</c> (a plan), mirroring the fields of the
/// <c>Product</c> schema in the OpenAPI contract that this integration consumes.
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

    public bool Taxable { get; set; }

    public DateTimeOffset? ArchivedAt { get; set; }

    public DateTimeOffset? CreatedAt { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }
}

/// <summary>Envelope for a single product, per the <c>Product-Response</c> schema.</summary>
public class MaxioProductResponse
{
    public MaxioProduct? Product { get; set; }
}
