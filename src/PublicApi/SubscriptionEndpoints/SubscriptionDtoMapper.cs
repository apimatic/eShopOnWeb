using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

internal static class SubscriptionDtoMapper
{
    public static BillingPlanDto ToDto(this BillingPlan plan) => new()
    {
        Id = plan.Id,
        Handle = plan.Handle,
        Name = plan.Name,
        PriceInCents = plan.PriceInCents,
        RequiresPaymentMethod = plan.RequiresPaymentMethod
    };

    public static BillingSubscriptionDto ToDto(this BillingSubscription subscription) => new()
    {
        Id = subscription.Id,
        ProductHandle = subscription.ProductHandle,
        ProductName = subscription.ProductName,
        ProductPriceInCents = subscription.ProductPriceInCents,
        State = subscription.State.ToString(),
        BalanceInCents = subscription.BalanceInCents,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        NextAssessmentAt = subscription.NextAssessmentAt
    };
}
