using Microsoft.eShopWeb.ApplicationCore.Models.Maxio;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

internal static class SubscriptionMapping
{
    public static SubscriptionPlanDto ToDto(this SubscriptionPlan plan) => new()
    {
        Handle = plan.Handle,
        Name = plan.Name,
        Description = plan.Description,
        Price = plan.PriceInCents / 100m,
        Interval = plan.Interval,
        IntervalUnit = plan.IntervalUnit,
        RequiresPaymentMethod = plan.RequiresPaymentMethod
    };

    public static SubscriptionDto ToDto(this SubscriptionEnrollment enrollment) => new()
    {
        MaxioSubscriptionId = enrollment.MaxioSubscriptionId,
        ProductHandle = enrollment.ProductHandle,
        ProductName = enrollment.ProductName,
        State = enrollment.State,
        Price = enrollment.PriceInCents / 100m,
        NextBillingDate = enrollment.NextAssessmentAt,
        CurrentPeriodEndsAt = enrollment.CurrentPeriodEndsAt,
        ActivatedAt = enrollment.ActivatedAt
    };
}
