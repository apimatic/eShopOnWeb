using System;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>API projection of a shopper's subscription, as recorded in Maxio.</summary>
public class SubscriptionDto
{
    public long Id { get; set; }
    public string State { get; set; } = string.Empty;
    public string PlanHandle { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public long PriceInCents { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string IntervalUnit { get; set; } = string.Empty;
    public string PriceDisplay { get; set; } = string.Empty;

    /// <summary>End of the current billing period — the next billing date.</summary>
    public DateTimeOffset? NextBillingDate { get; set; }

    public DateTimeOffset? CreatedAt { get; set; }
    public string? Reference { get; set; }

    public static SubscriptionDto From(CustomerSubscription subscription) => new()
    {
        Id = subscription.Id,
        State = subscription.State,
        PlanHandle = subscription.PlanHandle,
        PlanName = subscription.PlanName,
        PriceInCents = subscription.PriceInCents,
        Currency = subscription.Currency,
        IntervalUnit = subscription.IntervalUnit,
        PriceDisplay = SubscriptionFormatting.FormatPrice(subscription.PriceInCents, subscription.Currency)
            + SubscriptionFormatting.FormatPeriodSuffix(subscription.IntervalUnit),
        NextBillingDate = subscription.CurrentPeriodEndsAt,
        CreatedAt = subscription.CreatedAt,
        Reference = subscription.Reference,
    };
}
