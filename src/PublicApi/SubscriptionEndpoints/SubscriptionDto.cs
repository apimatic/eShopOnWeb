using System;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A shopper's subscription, as held by the billing system of record.
/// </summary>
public class SubscriptionDto
{
    /// <summary>Identifier of the subscription in the billing provider.</summary>
    public long Id { get; set; }

    /// <summary>Provider lifecycle state, e.g. <c>active</c>, <c>trialing</c>, <c>past_due</c>, <c>canceled</c>.</summary>
    public string State { get; set; } = string.Empty;

    /// <summary>True while the subscription is still in force.</summary>
    public bool IsLive { get; set; }

    public string? PlanHandle { get; set; }

    public string? PlanName { get; set; }

    /// <summary>Recurring price in minor currency units.</summary>
    public long PriceInCents { get; set; }

    /// <summary>Recurring price as a decimal amount.</summary>
    public decimal Price { get; set; }

    public string? Currency { get; set; }

    public int Interval { get; set; }

    public string? IntervalUnit { get; set; }

    /// <summary>Human readable price, e.g. <c>299.00 USD / month</c>.</summary>
    public string DisplayPrice { get; set; } = string.Empty;

    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }

    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

    /// <summary>When the provider will next bill this subscription.</summary>
    public DateTimeOffset? NextBillingAt { get; set; }

    public DateTimeOffset? ActivatedAt { get; set; }

    public DateTimeOffset? CanceledAt { get; set; }

    public DateTimeOffset? TrialEndsAt { get; set; }

    /// <summary>How the provider collects payment, e.g. <c>automatic</c> or <c>remittance</c>.</summary>
    public string? PaymentCollectionMethod { get; set; }

    /// <summary>Outstanding balance in minor currency units.</summary>
    public long BalanceInCents { get; set; }

    /// <summary>Balance as a decimal amount.</summary>
    public decimal Balance { get; set; }

    /// <summary>The shopper's customer id in the billing provider.</summary>
    public long BillingCustomerId { get; set; }

    /// <summary>The reference this application uses to identify the shopper to the billing provider.</summary>
    public string? BillingCustomerReference { get; set; }

    public static SubscriptionDto FromSubscription(CustomerSubscription subscription) => new()
    {
        Id = subscription.Id,
        State = subscription.State,
        IsLive = subscription.IsLive,
        PlanHandle = subscription.PlanHandle,
        PlanName = subscription.PlanName,
        PriceInCents = subscription.PriceInCents,
        Price = SubscriptionMoney.ToDecimal(subscription.PriceInCents),
        Currency = subscription.Currency,
        Interval = subscription.Interval,
        IntervalUnit = subscription.IntervalUnit,
        DisplayPrice = SubscriptionMoney.FormatRecurring(
            subscription.PriceInCents, subscription.Currency, subscription.Interval, subscription.IntervalUnit),
        CurrentPeriodStartedAt = subscription.CurrentPeriodStartedAt,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        NextBillingAt = subscription.NextBillingAt,
        ActivatedAt = subscription.ActivatedAt,
        CanceledAt = subscription.CanceledAt,
        TrialEndsAt = subscription.TrialEndsAt,
        PaymentCollectionMethod = subscription.PaymentCollectionMethod,
        BalanceInCents = subscription.BalanceInCents,
        Balance = SubscriptionMoney.ToDecimal(subscription.BalanceInCents),
        BillingCustomerId = subscription.BillingCustomerId,
        BillingCustomerReference = subscription.BillingCustomerReference
    };
}
