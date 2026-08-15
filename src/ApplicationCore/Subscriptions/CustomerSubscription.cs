using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A shopper's enrollment in a <see cref="SubscriptionPlan"/>, as reported by the billing
/// system of record.
/// </summary>
public record CustomerSubscription
{
    /// <summary>Identifier of the subscription in the billing system.</summary>
    public long? Id { get; init; }

    /// <summary>Handle of the plan the shopper is subscribed to.</summary>
    public string PlanHandle { get; init; } = string.Empty;

    /// <summary>Display name of the plan the shopper is subscribed to.</summary>
    public string PlanName { get; init; } = string.Empty;

    /// <summary>Recurring price expressed in the minor currency unit (e.g. cents).</summary>
    public long PriceInCents { get; init; }

    /// <summary>ISO currency code the price is expressed in (e.g. "USD").</summary>
    public string Currency { get; init; } = string.Empty;

    /// <summary>Lifecycle state of the subscription (e.g. "active", "pending", "canceled").</summary>
    public string State { get; init; } = string.Empty;

    /// <summary>When the subscription will next be assessed/billed, if known.</summary>
    public DateTimeOffset? NextBillingDate { get; init; }

    /// <summary>End of the current billing period, if known.</summary>
    public DateTimeOffset? CurrentPeriodEndsAt { get; init; }
}
