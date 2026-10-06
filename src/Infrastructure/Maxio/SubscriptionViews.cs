using System.Collections.Generic;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// A subscribable plan as read from the Maxio product catalog.
/// </summary>
public class SubscriptionPlanView
{
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int PriceInCents { get; set; }
    public int Interval { get; set; }
    public string? IntervalUnit { get; set; }
    public bool IsDefault { get; set; }
}

/// <summary>
/// A user's subscription as read from Maxio (the billing system of record).
/// </summary>
public class UserSubscriptionView
{
    public int SubscriptionId { get; set; }
    public string State { get; set; } = string.Empty;
    public string? Reference { get; set; }
    public string PlanHandle { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public int PriceInCents { get; set; }
    public int Interval { get; set; }
    public string? IntervalUnit { get; set; }
    public string? NextBillingDate { get; set; }
    public string? ActivatedAt { get; set; }
    public string? CreatedAt { get; set; }
    public string? CanceledAt { get; set; }
    public int MaxioCustomerId { get; set; }
}