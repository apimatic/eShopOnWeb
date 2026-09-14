using System;
using System.Globalization;
using Microsoft.eShopWeb.ApplicationCore.Entities.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A subscription that belongs to the authenticated shopper.
/// </summary>
public class CustomerSubscriptionDto
{
    public int Id { get; set; }

    /// <summary>Maxio lifecycle state (e.g. <c>active</c>, <c>past_due</c>, <c>canceled</c>).</summary>
    public string State { get; set; } = string.Empty;

    public string? PlanHandle { get; set; }
    public string? PlanName { get; set; }

    public int PriceInCents { get; set; }
    public decimal Price { get; set; }
    public string FormattedPrice { get; set; } = string.Empty;

    public int Interval { get; set; }
    public string? IntervalUnit { get; set; }

    /// <summary>The next date the subscription will be billed.</summary>
    public DateTimeOffset? NextBillingDate { get; set; }

    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }

    public string? PaymentCollectionMethod { get; set; }

    public static CustomerSubscriptionDto From(CustomerSubscription subscription)
    {
        var price = subscription.ProductPriceInCents / 100m;
        var frequency = SubscriptionFormatting.Frequency(subscription.Interval, subscription.IntervalUnit);
        return new CustomerSubscriptionDto
        {
            Id = subscription.Id,
            State = subscription.State,
            PlanHandle = subscription.PlanHandle,
            PlanName = subscription.PlanName,
            PriceInCents = subscription.ProductPriceInCents,
            Price = price,
            FormattedPrice = $"${price.ToString("0.00", CultureInfo.InvariantCulture)} / {frequency}",
            Interval = subscription.Interval,
            IntervalUnit = subscription.IntervalUnit,
            NextBillingDate = subscription.NextBillingDate,
            CurrentPeriodStartedAt = subscription.CurrentPeriodStartedAt,
            CreatedAt = subscription.CreatedAt,
            PaymentCollectionMethod = subscription.PaymentCollectionMethod
        };
    }
}
