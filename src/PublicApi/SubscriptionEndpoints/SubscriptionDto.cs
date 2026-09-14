using System;
using Microsoft.eShopWeb.ApplicationCore.SubscriptionBilling;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionDto
{
    public long SubscriptionId { get; set; }
    public string State { get; set; } = string.Empty;
    public long? CustomerId { get; set; }
    public long? ProductId { get; set; }
    public string? ProductHandle { get; set; }
    public string? ProductName { get; set; }
    public long ProductPriceInCents { get; set; }
    public decimal Price { get; set; }
    public long? BalanceInCents { get; set; }
    public string? Currency { get; set; }
    public int? Interval { get; set; }
    public string? IntervalUnit { get; set; }
    public string? PaymentCollectionMethod { get; set; }
    public DateTimeOffset? ActivatedAt { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

    public static SubscriptionDto FromSubscription(SubscriptionDetails subscription) => new()
    {
        SubscriptionId = subscription.SubscriptionId,
        State = subscription.State,
        CustomerId = subscription.CustomerId > 0 ? subscription.CustomerId : null,
        ProductId = subscription.ProductId,
        ProductHandle = subscription.ProductHandle,
        ProductName = subscription.ProductName,
        ProductPriceInCents = subscription.ProductPriceInCents,
        Price = subscription.Price,
        BalanceInCents = subscription.BalanceInCents,
        Currency = subscription.Currency,
        Interval = subscription.Interval,
        IntervalUnit = subscription.IntervalUnit,
        PaymentCollectionMethod = subscription.PaymentCollectionMethod,
        ActivatedAt = subscription.ActivatedAt,
        CreatedAt = subscription.CreatedAt,
        CurrentPeriodStartedAt = subscription.CurrentPeriodStartedAt,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt
    };
}
