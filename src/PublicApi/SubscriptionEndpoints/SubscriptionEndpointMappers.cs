using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

internal static class SubscriptionEndpointMappers
{
    public static SubscriptionDto ToDto(Subscription subscription) => new()
    {
        SubscriptionId = subscription.Id,
        ProductHandle = subscription.ProductHandle,
        State = subscription.State,
        CancelAtEndOfPeriod = subscription.CancelAtEndOfPeriod,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt
    };
}
