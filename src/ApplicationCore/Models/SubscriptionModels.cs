using System;

namespace Microsoft.eShopWeb.ApplicationCore.Models;

/// <summary>
/// A plan available for subscription, mapped from a Maxio product.
/// </summary>
public class SubscriptionPlanInfo
{
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal Price { get; set; }
    public int? Interval { get; set; }
    public string? IntervalUnit { get; set; }
    public bool RequireCreditCard { get; set; }
}

/// <summary>
/// A Maxio subscription belonging to an eShopOnWeb user.
/// </summary>
public class SubscriptionInfo
{
    public int SubscriptionId { get; set; }
    public string? Reference { get; set; }
    public string? PlanHandle { get; set; }
    public string? PlanName { get; set; }
    public string? State { get; set; }
    public decimal Price { get; set; }
    public DateTimeOffset? NextBillingDate { get; set; }
}
