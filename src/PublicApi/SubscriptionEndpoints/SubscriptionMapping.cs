using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

internal static class SubscriptionMapping
{
    public static SubscriptionDto ToDto(MaxioSubscription subscription) => new()
    {
        MaxioSubscriptionId = subscription.Id,
        PlanHandle = subscription.ProductHandle,
        PlanName = subscription.ProductName,
        Price = subscription.ProductPriceInCents / 100m,
        State = subscription.State,
        NextBillingDate = subscription.NextAssessmentAt ?? subscription.CurrentPeriodEndsAt
    };
}
