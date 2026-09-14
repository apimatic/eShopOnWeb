using System;

namespace Microsoft.eShopWeb.Infrastructure.Maxio.Contracts;

/// <summary>Maxio <c>Product Response</c> envelope.</summary>
public class ProductResponse
{
    public Product? Product { get; set; }
}

/// <summary>
/// Maxio <c>Product</c> schema. Only the fields this integration consumes are modelled; unknown members are
/// ignored by the deserializer so that additive changes in the API do not break the client.
/// </summary>
public class Product
{
    public int Id { get; set; }
    public string? Name { get; set; }

    /// <summary>The product API handle. Stable across re-seeds — the numeric id is not.</summary>
    public string? Handle { get; set; }

    public string? Description { get; set; }
    public string? AccountingCode { get; set; }

    /// <summary>The recurring product price, in integer cents.</summary>
    public long PriceInCents { get; set; }

    /// <summary>The numerical billing interval; combined with <see cref="IntervalUnit"/>.</summary>
    public int Interval { get; set; }

    /// <summary>"month" or "day".</summary>
    public string? IntervalUnit { get; set; }

    public long? TrialPriceInCents { get; set; }
    public int? TrialInterval { get; set; }
    public string? TrialIntervalUnit { get; set; }
    public long? InitialChargeInCents { get; set; }

    public int? ExpirationInterval { get; set; }
    public string? ExpirationIntervalUnit { get; set; }

    /// <summary>Timestamp indicating when this product was archived; <c>null</c> while it is sellable.</summary>
    public DateTimeOffset? ArchivedAt { get; set; }

    /// <summary>Whether a payment profile must be on file for customers signing up on this product.</summary>
    public bool RequireCreditCard { get; set; }

    public bool Taxable { get; set; }
    public int? VersionNumber { get; set; }
    public ProductFamily? ProductFamily { get; set; }
    public int? DefaultProductPricePointId { get; set; }
    public int? ProductPricePointId { get; set; }
    public string? ProductPricePointName { get; set; }
    public string? ProductPricePointHandle { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}
