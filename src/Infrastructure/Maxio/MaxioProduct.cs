using System;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// A product family in Maxio Advanced Billing (from the API's product_family object).
/// </summary>
public class MaxioProductFamily
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Handle { get; set; } = string.Empty;
}

/// <summary>
/// A product in Maxio Advanced Billing. Subscription plans are modeled as products.
/// Field names mirror the API's product object (snake_case in JSON, mapped here).
/// </summary>
public class MaxioProduct
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Handle { get; set; } = string.Empty;
    public string? Description { get; set; }
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
    public int? TrialInterval { get; set; }
    public string? TrialIntervalUnit { get; set; }
    public long? TrialPriceInCents { get; set; }
    public bool? RequireCreditCard { get; set; }
    public DateTime? ArchivedAt { get; set; }
    public bool? Taxable { get; set; }
    public string? ProductPricePointName { get; set; }
    public MaxioProductFamily ProductFamily { get; set; } = new();
}
