using System;

namespace Microsoft.eShopWeb.ApplicationCore.Billing;

/// <summary>
/// A shopper's enrollment in a <see cref="SubscriptionPlan"/>, as held by the billing system of record.
/// </summary>
public class CustomerSubscription
{
    public int Id { get; init; }

    public string PlanHandle { get; init; } = string.Empty;
    public string PlanName { get; init; } = string.Empty;

    /// <summary>Provider state, e.g. "active", "trialing", "canceled".</summary>
    public string State { get; init; } = string.Empty;

    /// <summary>Recurring price of the subscribed plan, in the smallest currency unit (cents).</summary>
    public long PriceInCents { get; init; }

    public string Currency { get; init; } = "USD";

    public int Interval { get; init; }
    public string IntervalUnit { get; init; } = string.Empty;

    /// <summary>
    /// When the provider will next bill this subscription. Null when the provider reports neither a
    /// current period end nor a next assessment date (e.g. a canceled subscription).
    /// </summary>
    public DateTimeOffset? NextBillingDate { get; init; }

    public DateTimeOffset? CurrentPeriodEndsAt { get; init; }
    public DateTimeOffset? ActivatedAt { get; init; }
    public DateTimeOffset? CanceledAt { get; init; }

    /// <summary>Outstanding balance in the smallest currency unit (cents).</summary>
    public long BalanceInCents { get; init; }

    public decimal Price => PriceInCents / 100m;
    public decimal Balance => BalanceInCents / 100m;

    /// <summary>
    /// True while the provider still considers this enrollment live — i.e. anything other than
    /// canceled, expired or failed-to-create. A live subscription blocks a second signup.
    /// </summary>
    public bool IsLive { get; init; }
}
