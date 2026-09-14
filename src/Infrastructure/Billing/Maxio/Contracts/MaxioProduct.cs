using System;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio.Contracts;

/// <summary>
/// Wrapper for a single product, matching the specification's <c>Product-Response</c> schema.
/// </summary>
public class ProductResponse
{
    public MaxioProduct? Product { get; set; }
}

/// <summary>
/// The specification's <c>Product</c> schema, limited to the fields this integration consumes.
/// A product is what eShopOnWeb publishes as a subscription plan.
/// </summary>
public class MaxioProduct
{
    public long Id { get; set; }

    public string? Name { get; set; }

    public string? Handle { get; set; }

    public string? Description { get; set; }

    public long PriceInCents { get; set; }

    public int Interval { get; set; }

    /// <summary>Either <c>month</c> or <c>day</c>.</summary>
    public string? IntervalUnit { get; set; }

    public long? InitialChargeInCents { get; set; }

    public long? TrialPriceInCents { get; set; }

    public int? TrialInterval { get; set; }

    public string? TrialIntervalUnit { get; set; }

    /// <summary>Whether a payment profile must be on file before a customer can sign up.</summary>
    public bool RequireCreditCard { get; set; }

    public bool Taxable { get; set; }

    public DateTimeOffset? ArchivedAt { get; set; }

    public MaxioProductFamily? ProductFamily { get; set; }

    public string? ProductPricePointHandle { get; set; }

    public long? ProductPricePointId { get; set; }
}

/// <summary>The specification's <c>Product-Family</c> schema.</summary>
public class MaxioProductFamily
{
    public long Id { get; set; }

    public string? Name { get; set; }

    public string? Handle { get; set; }

    public string? Description { get; set; }
}
