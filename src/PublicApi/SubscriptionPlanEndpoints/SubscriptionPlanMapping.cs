using Microsoft.eShopWeb.ApplicationCore.Entities.Billing;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionPlanEndpoints;

internal static class SubscriptionPlanMapping
{
    public static SubscriptionPlanDto ToDto(this SubscriptionPlan plan) => new()
    {
        Id = plan.Id,
        Handle = plan.Handle,
        Name = plan.Name,
        Description = plan.Description,
        PriceInCents = plan.PriceInCents,
        Price = plan.PriceInCents / 100m,
        Interval = plan.Interval,
        IntervalUnit = plan.IntervalUnit,
        RequireCreditCard = plan.RequireCreditCard,
        ProductFamilyHandle = plan.ProductFamilyHandle,
        ProductFamilyName = plan.ProductFamilyName
    };
}
