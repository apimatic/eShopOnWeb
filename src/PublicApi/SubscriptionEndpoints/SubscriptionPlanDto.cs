using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A plan a shopper can subscribe to. Plans are addressed by <see cref="Handle"/>; the numeric
/// provider id is informational only and is not stable across catalog re-seeds.
/// </summary>
public class SubscriptionPlanDto
{
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public long PriceInCents { get; set; }
    public decimal Price { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
    public bool RequiresPaymentMethod { get; set; }
    public int? TrialInterval { get; set; }
    public string? TrialIntervalUnit { get; set; }
    public long? TrialPriceInCents { get; set; }
    public bool Taxable { get; set; }
    public string? ProductFamilyHandle { get; set; }
    public int ProviderProductId { get; set; }
}
