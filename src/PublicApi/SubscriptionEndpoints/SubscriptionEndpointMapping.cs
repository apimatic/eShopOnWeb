using Microsoft.eShopWeb.ApplicationCore.Billing;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

internal static class SubscriptionEndpointMapping
{
    public static SubscriptionPlanDto ToDto(SubscriptionPlan plan) => new()
    {
        Handle = plan.Handle,
        Name = plan.Name,
        Description = plan.Description,
        Price = plan.Price,
        Interval = plan.Interval,
        IntervalUnit = plan.IntervalUnit,
        BillingPeriod = plan.BillingPeriod,
        Taxable = plan.Taxable,
        RequiresPaymentMethod = plan.RequiresPaymentMethod,
        ProductFamilyHandle = plan.ProductFamilyHandle
    };

    public static MySubscriptionDto ToDto(BillingSubscription subscription) => new()
    {
        SubscriptionId = subscription.Id,
        State = subscription.State,
        IsActive = subscription.IsActive,
        PlanHandle = subscription.PlanHandle,
        PlanName = subscription.PlanName,
        Price = subscription.Price,
        Currency = subscription.Currency,
        ActivatedAt = subscription.ActivatedAt,
        NextBillingAt = subscription.NextBillingAt
    };
}
