using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

internal static class SubscriptionMapping
{
    public static SubscriptionDto ToDto(this Subscription subscription) => new()
    {
        Id = subscription.Id,
        ProductHandle = subscription.ProductHandle,
        ProductName = subscription.ProductName,
        Price = subscription.Price,
        Status = subscription.Status.ToString(),
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        NextBillingAt = subscription.NextBillingAt,
        CancelAtEndOfPeriod = subscription.CancelAtEndOfPeriod,
        ScheduledCancellationAt = subscription.ScheduledCancellationAt
    };

    public static PlanDto ToDto(this BillingPlan plan) => new()
    {
        Handle = plan.Handle,
        Name = plan.Name,
        Price = plan.Price,
        IntervalCount = plan.IntervalCount,
        IntervalUnit = plan.IntervalUnit,
        RequiresPaymentMethod = plan.RequiresPaymentMethod
    };

    public static PlanChangePreviewDto ToDto(this PlanChangePreview preview) => new()
    {
        TargetProductHandle = preview.TargetProductHandle,
        PaymentDue = preview.PaymentDue,
        CreditApplied = preview.CreditApplied
    };
}
