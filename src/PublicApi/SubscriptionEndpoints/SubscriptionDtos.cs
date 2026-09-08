using Microsoft.eShopWeb.PublicApi.Maxio;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Maps Maxio wire models to the public subscription view models.
/// </summary>
internal static class SubscriptionDtos
{
    public static SubscriptionDto From(MaxioSubscription subscription) => new()
    {
        Id = subscription.Id,
        State = subscription.State ?? string.Empty,
        PlanHandle = subscription.Product?.Handle,
        PlanName = subscription.Product?.Name,
        Price = (subscription.ProductPriceInCents ?? subscription.Product?.PriceInCents ?? 0) / 100m,
        Interval = subscription.Product?.Interval ?? 0,
        IntervalUnit = subscription.Product?.IntervalUnit,
        Currency = string.IsNullOrWhiteSpace(subscription.Currency) ? "USD" : subscription.Currency,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        NextAssessmentAt = subscription.NextAssessmentAt,
        ActivatedAt = subscription.ActivatedAt,
        CreatedAt = subscription.CreatedAt,
        CanceledAt = subscription.CanceledAt
    };
}
