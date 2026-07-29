using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A shopper's subscription as reflected by the billing system. Provider-agnostic projection.
/// </summary>
public class CustomerSubscription
{
    /// <summary>Billing-system subscription id.</summary>
    public int Id { get; init; }

    /// <summary>Lifecycle state (e.g. "active", "trialing", "canceled").</summary>
    public string State { get; init; } = string.Empty;

    /// <summary>Handle of the plan the subscription is for.</summary>
    public string PlanHandle { get; init; } = string.Empty;

    /// <summary>Display name of the plan the subscription is for.</summary>
    public string PlanName { get; init; } = string.Empty;

    /// <summary>Recurring amount billed, in the smallest currency unit (e.g. cents).</summary>
    public long PriceInCents { get; init; }

    /// <summary>Recurring amount rendered in major units, culture-invariant (e.g. "299.00").</summary>
    public string FormattedPrice { get; init; } = string.Empty;

    /// <summary>Numeric billing interval (e.g. 1).</summary>
    public int Interval { get; init; }

    /// <summary>Billing interval unit (e.g. "month" or "day").</summary>
    public string IntervalUnit { get; init; } = string.Empty;

    /// <summary>End of the current billing period (the next regularly scheduled charge date).</summary>
    public DateTimeOffset? CurrentPeriodEndsAt { get; init; }

    /// <summary>When the next payment capture will be attempted (usually tracks the current period end).</summary>
    public DateTimeOffset? NextBillingAt { get; init; }

    /// <summary>When the subscription was created.</summary>
    public DateTimeOffset? CreatedAt { get; init; }

    /// <summary>Billing-system customer id that owns this subscription.</summary>
    public int CustomerId { get; init; }
}
