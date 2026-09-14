using Microsoft.eShopWeb.ApplicationCore.Billing;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

internal static class SubscriptionDtoFactory
{
    public static SubscriptionDto ToDto(Subscription subscription) => new SubscriptionDto
    {
        Id = subscription.Id,
        Reference = subscription.Reference,
        State = subscription.State,
        ProductId = subscription.ProductId,
        ProductHandle = subscription.ProductHandle,
        ProductName = subscription.ProductName,
        Price = subscription.Price,
        Currency = subscription.Currency,
        Interval = subscription.Interval,
        IntervalUnit = subscription.IntervalUnit,
        PaymentCollectionMethod = subscription.PaymentCollectionMethod,
        CustomerId = subscription.CustomerId,
        CreatedAt = subscription.CreatedAt,
        ActivatedAt = subscription.ActivatedAt,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        NextBillingDate = subscription.NextBillingDate
    };
}
