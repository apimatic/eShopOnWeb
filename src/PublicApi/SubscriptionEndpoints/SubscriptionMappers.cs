namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Maps subscription domain models to Public API DTOs.
/// </summary>
public static class SubscriptionMappers
{
    public static SubscriptionPlanDto MapPlan(ApplicationCore.Models.SubscriptionPlan plan) =>
        new()
        {
            ProductId = plan.ProductId,
            Handle = plan.Handle,
            Name = plan.Name,
            Description = plan.Description,
            PriceInCents = plan.PriceInCents,
            Price = plan.PriceInCents / 100m,
            Interval = plan.Interval,
            IntervalUnit = plan.IntervalUnit,
            HasTrial = plan.HasTrial,
            RequiresPaymentMethod = plan.RequiresPaymentMethod
        };

    public static UserSubscriptionDto MapSubscription(ApplicationCore.Models.UserSubscription subscription) =>
        new()
        {
            SubscriptionId = subscription.SubscriptionId,
            State = subscription.State,
            ProductHandle = subscription.ProductHandle,
            ProductName = subscription.ProductName,
            PriceInCents = subscription.PriceInCents,
            Price = subscription.PriceInCents / 100m,
            Interval = subscription.Interval,
            IntervalUnit = subscription.IntervalUnit,
            NextBillingDate = subscription.NextBillingDate,
            ActivatedAt = subscription.ActivatedAt,
            CanceledAt = subscription.CanceledAt
        };
}
