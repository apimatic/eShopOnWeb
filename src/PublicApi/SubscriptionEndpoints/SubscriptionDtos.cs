using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A subscription plan available for enrollment.
/// </summary>
public class SubscriptionPlanDto
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
/// A Maxio subscription belonging to the authenticated user.
/// </summary>
public class SubscriptionDto
{
    public int SubscriptionId { get; set; }
    public string? Reference { get; set; }
    public string? PlanHandle { get; set; }
    public string? PlanName { get; set; }
    public string? State { get; set; }
    public decimal Price { get; set; }
    public DateTimeOffset? NextBillingDate { get; set; }
}
