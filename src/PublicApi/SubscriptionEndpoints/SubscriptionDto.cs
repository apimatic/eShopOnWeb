using System;
using Microsoft.eShopWeb.Infrastructure.Maxio.Models;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A subscription as surfaced to the signed-in shopper. The subscription record and its state
/// are owned by Maxio Advanced Billing (system of record).
/// </summary>
public class SubscriptionDto
{
    /// <summary>The Maxio subscription id.</summary>
    public long Id { get; set; }

    /// <summary>Maxio subscription state, e.g. <c>active</c>, <c>trialing</c>, <c>past_due</c>.</summary>
    public string State { get; set; } = string.Empty;

    /// <summary>ISO currency code, e.g. <c>USD</c>.</summary>
    public string? Currency { get; set; }

    /// <summary>Outstanding balance in cents, if any.</summary>
    public long? BalanceInCents { get; set; }

    /// <summary>Recurring price of the subscribed product in cents.</summary>
    public long PriceInCents { get; set; }

    public DateTimeOffset? CreatedAt { get; set; }

    public DateTimeOffset? ActivatedAt { get; set; }

    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }

    /// <summary>When the current billing period ends (the next regularly scheduled renewal).</summary>
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

    /// <summary>When the next payment will be captured or retried.</summary>
    public DateTimeOffset? NextAssessmentAt { get; set; }

    public string? PaymentCollectionMethod { get; set; }

    /// <summary>The plan this subscription is on.</summary>
    public SubscriptionPlanDto? Plan { get; set; }

    internal static SubscriptionDto FromMaxio(MaxioSubscription subscription) => new SubscriptionDto
    {
        Id = subscription.Id,
        State = subscription.State,
        Currency = subscription.Currency,
        BalanceInCents = subscription.BalanceInCents,
        PriceInCents = subscription.ProductPriceInCents,
        CreatedAt = subscription.CreatedAt,
        ActivatedAt = subscription.ActivatedAt,
        CurrentPeriodStartedAt = subscription.CurrentPeriodStartedAt,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        NextAssessmentAt = subscription.NextAssessmentAt,
        PaymentCollectionMethod = subscription.PaymentCollectionMethod,
        Plan = subscription.Product is null ? null : SubscriptionPlanDto.FromMaxio(subscription.Product)
    };
}
