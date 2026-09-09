using System;

namespace Microsoft.eShopWeb.ApplicationCore.Models.Subscription;

/// <summary>
/// A purchasable subscription plan as read from the Maxio product catalog.
/// </summary>
public class SubscriptionPlan
{
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int PriceInCents { get; set; }
    public int Interval { get; set; }
    public string? IntervalUnit { get; set; }
    public string ProductFamilyHandle { get; set; } = string.Empty;
}
