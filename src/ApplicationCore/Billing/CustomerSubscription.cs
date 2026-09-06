using System;

namespace Microsoft.eShopWeb.ApplicationCore.Billing;

/// <summary>
/// A shopper's enrolment in a <see cref="SubscriptionPlan"/>, as held by the billing provider.
/// </summary>
public class CustomerSubscription
{
    /// <summary>The billing provider's identifier for the subscription.</summary>
    public long Id { get; set; }

    /// <summary>The reference this application assigned to the subscription; unique per provider site.</summary>
    public string? Reference { get; set; }

    /// <summary>Provider state, verbatim (e.g. "active", "trialing", "canceled").</summary>
    public string State { get; set; } = string.Empty;

    /// <summary>True while the subscription still entitles the shopper to the plan.</summary>
    public bool IsLive => SubscriptionStates.IsLive(State);

    public string? PlanHandle { get; set; }

    public string? PlanName { get; set; }

    public long PriceInCents { get; set; }

    public decimal Price => PriceInCents / 100m;

    public string? Currency { get; set; }

    public int Interval { get; set; }

    public string? IntervalUnit { get; set; }

    /// <summary>Start of the current billing period.</summary>
    public DateTimeOffset? CurrentPeriodStartsAt { get; set; }

    /// <summary>End of the current billing period.</summary>
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

    /// <summary>When the provider will next assess (bill) this subscription.</summary>
    public DateTimeOffset? NextBillingAt { get; set; }

    public DateTimeOffset? CreatedAt { get; set; }

    public DateTimeOffset? ActivatedAt { get; set; }

    public DateTimeOffset? CanceledAt { get; set; }

    public DateTimeOffset? ExpiresAt { get; set; }

    /// <summary>Outstanding balance in the smallest unit of <see cref="Currency"/>.</summary>
    public long BalanceInCents { get; set; }

    /// <summary>How the provider collects payment, e.g. "automatic" or "remittance".</summary>
    public string? PaymentCollectionMethod { get; set; }

    /// <summary>The billing provider's identifier for the customer that owns this subscription.</summary>
    public long CustomerId { get; set; }

    /// <summary>The reference this application assigned to the owning customer.</summary>
    public string? CustomerReference { get; set; }
}
