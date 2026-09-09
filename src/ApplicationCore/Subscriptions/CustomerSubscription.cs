using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A subscription that belongs to a shopper, as recorded in Maxio (Advanced Billing).
/// </summary>
public class CustomerSubscription
{
    /// <summary>Maxio subscription id.</summary>
    public int Id { get; init; }

    /// <summary>Lifecycle state as reported by Maxio (e.g. "active", "trialing", "canceled").</summary>
    public string State { get; init; } = string.Empty;

    /// <summary>Handle of the subscribed plan/product.</summary>
    public string? PlanHandle { get; init; }

    /// <summary>Display name of the subscribed plan/product.</summary>
    public string? PlanName { get; init; }

    /// <summary>The recurring price currently applied to this subscription, in cents.</summary>
    public int PriceInCents { get; init; }

    public decimal Price => PriceInCents / 100m;

    /// <summary>How payment is collected (e.g. "remittance", "automatic").</summary>
    public string? PaymentCollectionMethod { get; init; }

    /// <summary>Start of the current billing period.</summary>
    public DateTimeOffset? CurrentPeriodStartedAt { get; init; }

    /// <summary>End of the current billing period.</summary>
    public DateTimeOffset? CurrentPeriodEndsAt { get; init; }

    /// <summary>When the next charge/assessment is scheduled (the "next billing date").</summary>
    public DateTimeOffset? NextBillingAt { get; init; }

    public DateTimeOffset? CreatedAt { get; init; }
}
