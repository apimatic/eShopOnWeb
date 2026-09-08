using System;
using Microsoft.eShopWeb.PublicApi.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionDto
{
    public int? Id { get; set; }
    public string? ProductHandle { get; set; }
    public string? ProductName { get; set; }
    public decimal? Price { get; set; }
    public string? Currency { get; set; }
    public string? State { get; set; }
    public DateTimeOffset? CurrentPeriodStartedAt { get; set; }
    public DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }

    public static SubscriptionDto From(SubscriptionInfo subscription)
    {
        return new SubscriptionDto
        {
            Id = subscription.MaxioSubscriptionId,
            ProductHandle = subscription.ProductHandle,
            ProductName = subscription.ProductName,
            Price = subscription.Price,
            Currency = subscription.Currency,
            State = subscription.State,
            CurrentPeriodStartedAt = subscription.CurrentPeriodStartedAt,
            CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
            CreatedAt = subscription.CreatedAt
        };
    }
}
