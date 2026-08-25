using Microsoft.eShopWeb.PublicApi.Maxio;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public static class SubscriptionMapper
{
    public static SubscriptionDto ToDto(MaxioSubscription subscription)
    {
        return new SubscriptionDto
        {
            SubscriptionId = subscription.Id,
            State = subscription.State,
            PlanHandle = subscription.Product?.Handle ?? string.Empty,
            PlanName = subscription.Product?.Name ?? string.Empty,
            Price = subscription.ProductPriceInCents / 100m,
            Interval = subscription.Product?.Interval ?? 0,
            IntervalUnit = subscription.Product?.IntervalUnit ?? string.Empty,
            // current_period_ends_at is when the next regularly scheduled charge occurs.
            NextBillingAt = subscription.CurrentPeriodEndsAt ?? subscription.NextAssessmentAt,
            ActivatedAt = subscription.ActivatedAt,
            CreatedAt = subscription.CreatedAt
        };
    }
}
