using Microsoft.eShopWeb.ApplicationCore.SubscriptionBilling;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Maps billing domain models onto the public API DTOs.
/// </summary>
public static class SubscriptionApiMapper
{
    public static SubscriptionPlanDto ToDto(BillingPlan plan) => new()
    {
        Id = plan.Id,
        Handle = plan.Handle,
        Name = plan.Name,
        Description = plan.Description,
        Price = ToDecimalPrice(plan.PriceInCents),
        PriceDisplay = FormatPrice(plan.PriceInCents, plan.Interval, plan.IntervalUnit),
        Interval = plan.Interval,
        IntervalUnit = plan.IntervalUnit,
        PaymentMethodRequired = plan.RequiresPaymentProfile,
    };

    public static SubscriptionDto ToDto(BillingSubscription subscription) => new()
    {
        Id = subscription.Id,
        State = subscription.State,
        PlanHandle = subscription.PlanHandle,
        PlanName = subscription.PlanName,
        Price = ToDecimalPrice(subscription.PriceInCents),
        PriceDisplay = FormatPrice(subscription.PriceInCents, subscription.Interval, subscription.IntervalUnit),
        Interval = subscription.Interval,
        IntervalUnit = subscription.IntervalUnit,
        PaymentCollectionMethod = subscription.PaymentCollectionMethod,
        CurrentPeriodStartsAt = subscription.CurrentPeriodStartsAt,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        NextBillingDate = subscription.NextBillingDate,
        ActivatedAt = subscription.ActivatedAt,
        CreatedAt = subscription.CreatedAt,
    };

    private static decimal ToDecimalPrice(long priceInCents) => priceInCents / 100m;

    private static string FormatPrice(long priceInCents, int interval, string intervalUnit)
    {
        var unit = string.IsNullOrWhiteSpace(intervalUnit) ? "month" : intervalUnit.Trim().ToLowerInvariant();
        var period = interval > 1 ? $"{interval} {unit}s" : unit;
        return $"{ToDecimalPrice(priceInCents):0.00} / {period}";
    }
}
