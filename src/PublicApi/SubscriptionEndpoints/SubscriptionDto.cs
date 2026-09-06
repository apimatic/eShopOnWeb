using System;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A shopper's subscription, as held by the billing system of record.
/// </summary>
public class SubscriptionDto
{
    /// <summary>Identifier of the subscription in the billing system.</summary>
    public int Id { get; set; }

    /// <summary>Reference this store assigned to the subscription; stable per user and plan.</summary>
    public string? Reference { get; set; }

    /// <summary>Billing state, e.g. "active", "trialing", "past_due", "canceled".</summary>
    public string State { get; set; } = string.Empty;

    /// <summary>True while the subscription is an ongoing enrollment (not canceled or expired).</summary>
    public bool IsLive { get; set; }

    public string? PlanHandle { get; set; }

    public string? PlanName { get; set; }

    public int? PriceInCents { get; set; }

    public decimal? Price { get; set; }

    public string? Currency { get; set; }

    public string BillingPeriod { get; set; } = string.Empty;

    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }

    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

    /// <summary>When the next billing attempt is scheduled.</summary>
    public DateTimeOffset? NextBillingAt { get; set; }

    public DateTimeOffset? ActivatedAt { get; set; }

    public DateTimeOffset? CanceledAt { get; set; }

    public DateTimeOffset? CreatedAt { get; set; }

    /// <summary>Outstanding balance in minor units.</summary>
    public int? BalanceInCents { get; set; }

    /// <summary>Identifier of the billing customer this store created for the signed-in user.</summary>
    public int CustomerId { get; set; }

    /// <summary>Reference this store assigned to the billing customer.</summary>
    public string? CustomerReference { get; set; }

    public string? CustomerEmail { get; set; }

    public static SubscriptionDto From(CustomerSubscription subscription) => new()
    {
        Id = subscription.Id,
        Reference = subscription.Reference,
        State = subscription.State,
        IsLive = subscription.IsLive,
        PlanHandle = subscription.PlanHandle,
        PlanName = subscription.PlanName,
        PriceInCents = subscription.PriceInCents,
        Price = subscription.Price,
        Currency = subscription.Currency,
        BillingPeriod = SubscriptionPlanDto.FormatPeriod(subscription.Interval, subscription.IntervalUnit),
        CurrentPeriodStartedAt = subscription.CurrentPeriodStartedAt,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        NextBillingAt = subscription.NextBillingAt,
        ActivatedAt = subscription.ActivatedAt,
        CanceledAt = subscription.CanceledAt,
        CreatedAt = subscription.CreatedAt,
        BalanceInCents = subscription.BalanceInCents,
        CustomerId = subscription.Customer.Id,
        CustomerReference = subscription.Customer.Reference,
        CustomerEmail = subscription.Customer.Email
    };
}
