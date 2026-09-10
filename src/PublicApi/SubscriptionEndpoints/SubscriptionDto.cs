using System;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// API representation of a shopper's subscription, as reported by Maxio.
/// </summary>
public class SubscriptionDto
{
    public int Id { get; set; }
    public string State { get; set; } = string.Empty;
    public string PlanHandle { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;

    public long PricePerPeriodInCents { get; set; }
    public decimal Price { get; set; }
    public string Currency { get; set; } = "USD";

    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;

    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

    /// <summary>When the next renewal charge is scheduled (the shopper's "next billing date").</summary>
    public DateTimeOffset? NextBillingAt { get; set; }

    public DateTimeOffset? CreatedAt { get; set; }

    public string FormattedPrice { get; set; } = string.Empty;

    public static SubscriptionDto FromDomain(CustomerSubscription subscription)
    {
        var price = subscription.PricePerPeriodInCents / 100m;
        return new SubscriptionDto
        {
            Id = subscription.Id,
            State = subscription.State,
            PlanHandle = subscription.PlanHandle,
            PlanName = subscription.PlanName,
            PricePerPeriodInCents = subscription.PricePerPeriodInCents,
            Price = price,
            Currency = subscription.CurrencyCode,
            Interval = subscription.Interval,
            IntervalUnit = subscription.IntervalUnit,
            CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
            NextBillingAt = subscription.NextBillingAt,
            CreatedAt = subscription.CreatedAt,
            FormattedPrice = SubscriptionFormatting.FormatPrice(price, subscription.Interval, subscription.IntervalUnit)
        };
    }
}
