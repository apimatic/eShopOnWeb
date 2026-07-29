using System;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>API projection of a shopper's subscription.</summary>
public class CustomerSubscriptionDto
{
    public long Id { get; set; }
    public long CustomerId { get; set; }
    public string PlanHandle { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public int PriceInCents { get; set; }
    public string FormattedPrice { get; set; } = string.Empty;
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;
    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }

    /// <summary>The next billing / renewal date.</summary>
    public DateTimeOffset? NextBillingAt { get; set; }
    public DateTimeOffset? ActivatedAt { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public string PaymentCollectionMethod { get; set; } = string.Empty;

    public static CustomerSubscriptionDto FromDomain(CustomerSubscription subscription) => new()
    {
        Id = subscription.Id,
        CustomerId = subscription.CustomerId,
        PlanHandle = subscription.PlanHandle,
        PlanName = subscription.PlanName,
        State = subscription.State,
        PriceInCents = subscription.PriceInCents,
        FormattedPrice = subscription.FormattedPrice,
        Interval = subscription.Interval,
        IntervalUnit = subscription.IntervalUnit,
        CurrentPeriodStartedAt = subscription.CurrentPeriodStartedAt,
        NextBillingAt = subscription.NextBillingAt,
        ActivatedAt = subscription.ActivatedAt,
        CreatedAt = subscription.CreatedAt,
        PaymentCollectionMethod = subscription.PaymentCollectionMethod,
    };
}
