using System;

namespace Microsoft.eShopWeb.ApplicationCore.Models.Subscriptions;

/// <summary>
/// A plan that shoppers can subscribe to.
/// </summary>
public class SubscriptionPlanInfo
{
    public string Handle { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>Recurring price per billing period.</summary>
    public decimal Price { get; set; }

    /// <summary>Length of the billing period, e.g. 1.</summary>
    public int Interval { get; set; }

    /// <summary>Unit of the billing period, e.g. month.</summary>
    public string IntervalUnit { get; set; } = string.Empty;

    /// <summary>True when this plan is used when no plan handle is supplied.</summary>
    public bool IsDefault { get; set; }
}

/// <summary>
/// A user's subscription as reported by the billing system.
/// </summary>
public class UserSubscriptionInfo
{
    public long MaxioSubscriptionId { get; set; }

    public long MaxioCustomerId { get; set; }

    public string PlanHandle { get; set; } = string.Empty;

    public string PlanName { get; set; } = string.Empty;

    public string State { get; set; } = string.Empty;

    public decimal Price { get; set; }

    public string? Currency { get; set; }

    /// <summary>Date the next billing event is assessed.</summary>
    public DateTime? NextBillingDate { get; set; }

    public DateTime? CreatedAt { get; set; }
}

/// <summary>
/// Outcome of enrolling a user in a plan.
/// </summary>
public class SubscribeResult
{
    public SubscribeResult(UserSubscriptionInfo subscription, bool alreadySubscribed)
    {
        Subscription = subscription;
        AlreadySubscribed = alreadySubscribed;
    }

    public UserSubscriptionInfo Subscription { get; }

    /// <summary>True when the user already held a live subscription for the plan.</summary>
    public bool AlreadySubscribed { get; }
}
