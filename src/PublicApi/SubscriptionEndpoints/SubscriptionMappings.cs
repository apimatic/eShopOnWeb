using Microsoft.eShopWeb.PublicApi.Maxio;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Maps Maxio (Advanced Billing) objects onto the public subscription DTOs.
/// </summary>
public static class SubscriptionMappings
{
    public static SubscriptionPlanDto ToDto(MaxioProduct plan, string currency)
    {
        var dto = new SubscriptionPlanDto
        {
            Handle = plan.Handle ?? string.Empty,
            Name = plan.Name ?? string.Empty,
            Description = plan.Description,
            Price = ToDecimal(plan.PriceInCents),
            Currency = currency,
            Interval = plan.Interval ?? 1,
            IntervalUnit = plan.IntervalUnit ?? "month"
        };

        return dto;
    }

    public static SubscriptionDto ToDto(MaxioSubscription subscription)
    {
        var product = subscription.Product;
        var dto = new SubscriptionDto
        {
            SubscriptionId = subscription.Id,
            Reference = subscription.Reference,
            State = subscription.State,
            PlanHandle = product?.Handle,
            PlanName = product?.Name,
            Price = ToDecimal(subscription.ProductPriceInCents),
            Currency = subscription.Currency,
            Interval = product?.Interval ?? 1,
            IntervalUnit = product?.IntervalUnit ?? "month",
            ActivatedAt = subscription.ActivatedAt,
            CurrentPeriodStartedAt = subscription.CurrentPeriodStartedAt,
            NextBillingDate = subscription.CurrentPeriodEndsAt,
            CreatedAt = subscription.CreatedAt
        };

        return dto;
    }

    internal static decimal ToDecimal(long cents) => cents / 100m;
}
