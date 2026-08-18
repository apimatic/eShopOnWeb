using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ShopperSubscriptionDto
{
    public int Id { get; set; }
    public string? Reference { get; set; }
    public string PlanHandle { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public long PriceInCents { get; set; }
    public decimal Price { get; set; }
    public string State { get; set; } = string.Empty;
    public DateTimeOffset? NextBillingAt { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public string? Currency { get; set; }
}

public static class ShopperSubscriptionDtoMapper
{
    public static ShopperSubscriptionDto From(ApplicationCore.Billing.ShopperSubscription subscription)
    {
        return new ShopperSubscriptionDto
        {
            Id = subscription.Id,
            Reference = subscription.Reference,
            PlanHandle = subscription.PlanHandle,
            PlanName = subscription.PlanName,
            PriceInCents = subscription.PriceInCents,
            Price = subscription.PriceInCents / 100m,
            State = subscription.State,
            NextBillingAt = subscription.NextBillingAt,
            CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
            Currency = subscription.Currency
        };
    }
}
