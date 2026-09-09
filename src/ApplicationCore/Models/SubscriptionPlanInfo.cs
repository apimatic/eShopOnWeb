using System;

namespace Microsoft.eShopWeb.ApplicationCore.Models;

/// <summary>
/// A purchasable subscription plan as surfaced to API consumers. Plans are
/// Maxio products inside the configured product family.
/// </summary>
public class SubscriptionPlanInfo
{
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal Price { get; set; }
    public long PriceInCents { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
    public bool RequireCreditCard { get; set; }
}
