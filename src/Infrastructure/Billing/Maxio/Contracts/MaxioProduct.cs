using System;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio.Contracts;

/// <summary>Wire model for the specification's <c>Product Response</c> schema.</summary>
public class MaxioProductResponse
{
    public MaxioProduct? Product { get; set; }
}

/// <summary>Wire model for the specification's <c>Product</c> schema (only the fields this integration uses).</summary>
public class MaxioProduct
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public string? Handle { get; set; }
    public string? Description { get; set; }
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string? IntervalUnit { get; set; }
    public long? InitialChargeInCents { get; set; }
    public long? TrialPriceInCents { get; set; }
    public int? TrialInterval { get; set; }
    public string? TrialIntervalUnit { get; set; }
    public DateTimeOffset? ArchivedAt { get; set; }
    public bool RequireCreditCard { get; set; }
    public string? ProductPricePointName { get; set; }
    public string? ProductPricePointHandle { get; set; }
    public MaxioProductFamily? ProductFamily { get; set; }
}

/// <summary>Wire model for the specification's <c>Product Family</c> schema.</summary>
public class MaxioProductFamily
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public string? Handle { get; set; }
    public string? Description { get; set; }
}
