using System;
using Microsoft.eShopWeb.PublicApi.Maxio;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionDto
{
    public long SubscriptionId { get; set; }
    public string ProductHandle { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string Currency { get; set; } = string.Empty;
    public DateTimeOffset? ActivatedAt { get; set; }
    public DateTimeOffset? NextBillingDate { get; set; }

    public static SubscriptionDto FromMaxio(MaxioSubscription subscription)
    {
        return new SubscriptionDto
        {
            SubscriptionId = subscription.Id,
            ProductHandle = subscription.Product?.Handle ?? string.Empty,
            ProductName = subscription.Product?.Name ?? string.Empty,
            State = subscription.State ?? string.Empty,
            Price = subscription.ProductPriceInCents / 100m,
            Currency = subscription.Currency ?? string.Empty,
            ActivatedAt = subscription.ActivatedAt,
            NextBillingDate = subscription.CurrentPeriodEndsAt
        };
    }
}
