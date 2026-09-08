using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.PublicApi.Maxio;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionDto
{
    public long Id { get; set; }
    public string PlanHandle { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string State { get; set; } = string.Empty;
    public DateTimeOffset? NextBillingAt { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }

    public static SubscriptionDto From(MaxioSubscription subscription)
    {
        return new SubscriptionDto
        {
            Id = subscription.Id,
            PlanHandle = subscription.ProductHandle ?? string.Empty,
            PlanName = subscription.ProductName ?? string.Empty,
            Price = (subscription.PriceInCents ?? 0) / 100m,
            State = subscription.State,
            NextBillingAt = subscription.CurrentPeriodEndsAt,
            CreatedAt = subscription.CreatedAt
        };
    }
}
