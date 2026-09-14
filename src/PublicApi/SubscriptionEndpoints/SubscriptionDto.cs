using System;
using Microsoft.eShopWeb.PublicApi.MaxioBilling;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionDto
{
    public long Id { get; set; }

    public string? State { get; set; }

    public long? PlanId { get; set; }

    public string? PlanName { get; set; }

    public string? PlanHandle { get; set; }

    public long? PriceInCents { get; set; }

    public decimal Price { get; set; }

    public string? Currency { get; set; }

    public DateTimeOffset? NextBillingDate { get; set; }

    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }

    public DateTimeOffset? ActivatedAt { get; set; }

    public DateTimeOffset? CreatedAt { get; set; }

    public static SubscriptionDto FromMaxio(MaxioSubscription subscription)
    {
        long? priceInCents = subscription.Product?.PriceInCents ?? subscription.ProductPriceInCents;

        return new SubscriptionDto
        {
            Id = subscription.Id,
            State = subscription.State,
            PlanId = subscription.Product?.Id,
            PlanName = subscription.Product?.Name,
            PlanHandle = subscription.Product?.Handle,
            PriceInCents = priceInCents,
            Price = priceInCents is null ? 0 : priceInCents.Value / 100m,
            Currency = subscription.Currency,
            NextBillingDate = subscription.CurrentPeriodEndsAt ?? subscription.NextAssessmentAt,
            CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
            ActivatedAt = subscription.ActivatedAt,
            CreatedAt = subscription.CreatedAt
        };
    }
}
