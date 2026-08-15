using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A shopper's subscription as recorded by the Maxio billing system. Read model owned
/// by the application; contains no SDK types.
/// </summary>
public class CustomerSubscription
{
    /// <summary>Maxio subscription identifier.</summary>
    public long SubscriptionId { get; init; }

    /// <summary>Lifecycle state reported by Maxio (e.g. <c>active</c>, <c>trialing</c>, <c>canceled</c>).</summary>
    public string State { get; init; } = string.Empty;

    /// <summary>Handle of the plan the subscription is enrolled in.</summary>
    public string PlanHandle { get; init; } = string.Empty;

    /// <summary>Display name of the plan.</summary>
    public string PlanName { get; init; } = string.Empty;

    /// <summary>Recurring price, in the currency's minor units (cents).</summary>
    public long PriceInCents { get; init; }

    /// <summary>ISO currency code (e.g. <c>USD</c>).</summary>
    public string Currency { get; init; } = "USD";

    /// <summary>When the next billing/assessment occurs, if scheduled.</summary>
    public DateTimeOffset? NextBillingAt { get; init; }

    /// <summary>When the subscription was created in Maxio.</summary>
    public DateTimeOffset? CreatedAt { get; init; }

    /// <summary>Maxio customer identifier the subscription belongs to.</summary>
    public long CustomerId { get; init; }
}
