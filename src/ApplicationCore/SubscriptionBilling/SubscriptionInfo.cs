using System;

namespace Microsoft.eShopWeb.ApplicationCore.SubscriptionBilling;

/// <summary>
/// A subscription as recorded in Maxio Advanced Billing (the billing system of record).
/// </summary>
public sealed class SubscriptionInfo
{
    /// <summary>Maxio subscription id. Numeric ids are not stable across Maxio re-seeds.</summary>
    public long SubscriptionId { get; init; }

    /// <summary>Maxio subscription state (e.g. "active", "trialing", "canceled").</summary>
    public string State { get; init; } = string.Empty;

    public string PlanHandle { get; init; } = string.Empty;

    public string PlanName { get; init; } = string.Empty;

    /// <summary>Recurring price of the subscription, in cents.</summary>
    public long PriceInCents { get; init; }

    /// <summary>Recurring price as a decimal amount (e.g. 299.00).</summary>
    public decimal Price => PriceInCents / 100m;

    /// <summary>When the next billing date falls (end of the current period).</summary>
    public DateTimeOffset? NextBillingDate { get; init; }

    public DateTimeOffset? ActivatedAt { get; init; }

    public DateTimeOffset? CreatedAt { get; init; }

    /// <summary>True when the subscription was found already existing instead of newly created.</summary>
    public bool AlreadySubscribed { get; init; }
}
