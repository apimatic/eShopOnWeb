using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A subscription held by the caller, as returned by the PublicApi.
/// </summary>
public class SubscriptionDto
{
    /// <summary>The billing system's subscription id.</summary>
    public int Id { get; set; }

    /// <summary>The app-provided reference stored on the billing subscription.</summary>
    public string Reference { get; set; } = string.Empty;

    /// <summary>Subscription state, e.g. "active".</summary>
    public string State { get; set; } = string.Empty;

    public string PlanHandle { get; set; } = string.Empty;

    public string PlanName { get; set; } = string.Empty;

    /// <summary>Recurring price in major currency units.</summary>
    public decimal Price { get; set; }

    public string? Currency { get; set; }

    /// <summary>UTC timestamp of the next scheduled billing, when known.</summary>
    public DateTimeOffset? NextBillingDate { get; set; }

    public DateTimeOffset? ActivatedAt { get; set; }

    public DateTimeOffset? CreatedAt { get; set; }
}
