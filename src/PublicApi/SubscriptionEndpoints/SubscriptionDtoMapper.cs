using Microsoft.eShopWeb.Infrastructure.Maxio;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public static class SubscriptionDtoMapper
{
    public static SubscriptionDto ToDto(MaxioSubscription subscription, bool created) => new()
    {
        SubscriptionId = subscription.Id,
        PlanHandle = subscription.Product.Handle,
        PlanName = subscription.Product.Name,
        PriceInCents = subscription.Product.PriceInCents,
        BillingInterval = subscription.Product.Interval,
        BillingIntervalUnit = subscription.Product.IntervalUnit,
        State = subscription.State,
        NextBillingDate = subscription.NextAssessmentAt,
        PaymentCollectionMethod = subscription.PaymentCollectionMethod,
        Created = created
    };
}
