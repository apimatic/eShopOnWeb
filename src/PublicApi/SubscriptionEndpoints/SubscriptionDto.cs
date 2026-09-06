using System;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A shopper's subscription as held by the billing system.
/// </summary>
public class SubscriptionDto
{
    /// <summary>The billing system's subscription id.</summary>
    public int Id { get; set; }

    /// <summary>Billing state, e.g. "active", "trialing", "past_due", "canceled".</summary>
    public string State { get; set; } = string.Empty;

    /// <summary>True while the subscription still entitles the shopper to the plan.</summary>
    public bool IsActive { get; set; }

    public string? PlanHandle { get; set; }
    public string? PlanName { get; set; }

    /// <summary>The amount charged each period, in the smallest currency unit.</summary>
    public long PriceInCents { get; set; }

    /// <summary>The amount charged each period, as a decimal amount.</summary>
    public decimal Price { get; set; }

    /// <summary>The billing site's currency, e.g. "USD".</summary>
    public string Currency { get; set; } = string.Empty;

    /// <summary>How often the subscription renews, e.g. "1 month".</summary>
    public string BillingPeriod { get; set; } = string.Empty;

    /// <summary>When the next renewal charge will be attempted.</summary>
    public DateTimeOffset? NextBillingAt { get; set; }

    public DateTimeOffset? CurrentPeriodStartsAt { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? ActivatedAt { get; set; }
    public DateTimeOffset? CanceledAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Outstanding balance in the smallest currency unit; 0 when nothing is owed.</summary>
    public long BalanceInCents { get; set; }

    public int CustomerId { get; set; }
    public string? CustomerReference { get; set; }

    public static SubscriptionDto FromSubscription(BillingSubscription subscription) => new()
    {
        Id = subscription.Id,
        State = subscription.State,
        IsActive = subscription.IsLive,
        PlanHandle = subscription.PlanHandle,
        PlanName = subscription.PlanName,
        PriceInCents = subscription.PriceInCents,
        Price = subscription.PriceInCents / 100m,
        Currency = subscription.Currency,
        BillingPeriod = SubscriptionPlanDto.FormatPeriod(subscription.PlanInterval, subscription.PlanIntervalUnit),
        NextBillingAt = subscription.NextBillingAt,
        CurrentPeriodStartsAt = subscription.CurrentPeriodStartsAt,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        ActivatedAt = subscription.ActivatedAt,
        CanceledAt = subscription.CanceledAt,
        CreatedAt = subscription.CreatedAt,
        BalanceInCents = subscription.BalanceInCents,
        CustomerId = subscription.CustomerId,
        CustomerReference = subscription.CustomerReference
    };
}
