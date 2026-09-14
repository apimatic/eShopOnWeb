using Microsoft.eShopWeb.PublicApi.Maxio;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

internal static class SubscriptionDtoMappings
{
    public static SubscriptionPlanDto ToSubscriptionPlanDto(this MaxioProduct product) => new()
    {
        Id = product.Id,
        Handle = product.Handle,
        Name = product.Name,
        Description = product.Description,
        PriceInCents = product.PriceInCents,
        Interval = product.Interval,
        IntervalUnit = product.IntervalUnit
    };

    public static SubscriptionDto ToSubscriptionDto(this MaxioSubscription subscription) => new()
    {
        SubscriptionId = subscription.Id,
        State = subscription.State,
        BalanceInCents = subscription.BalanceInCents,
        Currency = subscription.Currency,
        PaymentCollectionMethod = subscription.PaymentCollectionMethod,
        CreatedAt = subscription.CreatedAt,
        CurrentPeriodStartedAt = subscription.CurrentPeriodStartedAt,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        Plan = subscription.Product?.ToSubscriptionPlanDto()
    };
}
