using System;

namespace Microsoft.eShopWeb.Infrastructure.Maxio.Contracts;

/// <summary>Maxio <c>Product Family</c> (<c>components/schemas/Product-Family.yaml</c>).</summary>
public record MaxioProductFamily
{
    public int Id { get; init; }
    public string? Name { get; init; }
    public string? Handle { get; init; }
    public string? Description { get; init; }
}

/// <summary>
/// Maxio <c>Product</c> (<c>components/schemas/Product.yaml</c>) - a subscribable plan.
/// </summary>
public record MaxioProduct
{
    public int Id { get; init; }
    public string? Name { get; init; }
    public string? Handle { get; init; }
    public string? Description { get; init; }
    public long PriceInCents { get; init; }
    public int Interval { get; init; }
    public string? IntervalUnit { get; init; }
    public long? InitialChargeInCents { get; init; }
    public long? TrialPriceInCents { get; init; }
    public int? TrialInterval { get; init; }
    public string? TrialIntervalUnit { get; init; }
    public int? ExpirationInterval { get; init; }
    public string? ExpirationIntervalUnit { get; init; }
    public DateTimeOffset? ArchivedAt { get; init; }
    public bool RequireCreditCard { get; init; }
    public bool Taxable { get; init; }
    public int? DefaultProductPricePointId { get; init; }
    public int? ProductPricePointId { get; init; }
    public string? ProductPricePointName { get; init; }
    public string? ProductPricePointHandle { get; init; }
    public MaxioProductFamily? ProductFamily { get; init; }
    public DateTimeOffset? CreatedAt { get; init; }
    public DateTimeOffset? UpdatedAt { get; init; }
}

/// <summary>Maxio <c>Product Response</c> (<c>components/schemas/Product-Response.yaml</c>).</summary>
public record MaxioProductResponse
{
    public MaxioProduct? Product { get; init; }
}
