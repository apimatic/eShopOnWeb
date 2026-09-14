using System;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// A customer's subscription, as reported by Maxio.
/// </summary>
public class CustomerSubscriptionDto
{
    public int Id { get; set; }
    public string State { get; set; } = string.Empty;
    public string? PlanHandle { get; set; }
    public string? PlanName { get; set; }
    public long PriceInCents { get; set; }
    public string FormattedPrice { get; set; } = string.Empty;
    public int Interval { get; set; }
    public string? IntervalUnit { get; set; }
    public string BillingSummary { get; set; } = string.Empty;
    public DateTimeOffset? NextBillingAt { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public int CustomerId { get; set; }

    public static CustomerSubscriptionDto FromDomain(CustomerSubscription subscription) => new()
    {
        Id = subscription.Id,
        State = subscription.State,
        PlanHandle = subscription.PlanHandle,
        PlanName = subscription.PlanName,
        PriceInCents = subscription.ProductPriceInCents,
        FormattedPrice = SubscriptionPresentation.FormatPrice(subscription.ProductPriceInCents),
        Interval = subscription.Interval,
        IntervalUnit = subscription.IntervalUnit,
        BillingSummary = SubscriptionPresentation.FormatBillingSummary(
            subscription.ProductPriceInCents, subscription.Interval, subscription.IntervalUnit),
        NextBillingAt = subscription.NextBillingAt,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        CreatedAt = subscription.CreatedAt,
        CustomerId = subscription.CustomerId
    };
}
