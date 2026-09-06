using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A subscription held by an eShopOnWeb account, as reported by the billing provider.
/// </summary>
public class CustomerSubscription
{
    /// <summary>Provider-assigned subscription id.</summary>
    public long Id { get; init; }

    /// <summary>Provider subscription state, e.g. <c>active</c>, <c>trialing</c>, <c>canceled</c>.</summary>
    public string State { get; init; } = string.Empty;

    /// <summary>True while the subscription still entitles the shopper to the plan.</summary>
    public bool IsLive => SubscriptionStates.IsLive(State);

    public string PlanHandle { get; init; } = string.Empty;

    public string PlanName { get; init; } = string.Empty;

    /// <summary>Recurring price actually charged for this subscription, in the smallest currency unit.</summary>
    public long PriceInCents { get; init; }

    public string Currency { get; init; } = string.Empty;

    /// <summary>Outstanding balance in the smallest currency unit.</summary>
    public long BalanceInCents { get; init; }

    /// <summary>How the provider collects payment, e.g. <c>automatic</c> or <c>remittance</c>.</summary>
    public string? PaymentCollectionMethod { get; init; }

    public DateTimeOffset? CurrentPeriodStartedAt { get; init; }

    public DateTimeOffset? CurrentPeriodEndsAt { get; init; }

    /// <summary>When the provider will next assess (bill) this subscription.</summary>
    public DateTimeOffset? NextBillingAt { get; init; }

    public DateTimeOffset? ActivatedAt { get; init; }

    public DateTimeOffset? CanceledAt { get; init; }

    public DateTimeOffset? CreatedAt { get; init; }

    /// <summary>Provider-assigned customer id owning this subscription.</summary>
    public long CustomerId { get; init; }

    /// <summary>The eShopOnWeb-owned reference stored on the provider's customer record.</summary>
    public string? CustomerReference { get; init; }

    /// <summary>Recurring price as a decimal amount (e.g. 299.00).</summary>
    public decimal Price => PriceInCents / 100m;
}
