using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A subscription plan available for eShopOnWeb shoppers (a Maxio Advanced Billing product).
/// </summary>
public class SubscriptionPlanDto
{
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal Price { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
    public bool RequiresPaymentMethod { get; set; }
}
