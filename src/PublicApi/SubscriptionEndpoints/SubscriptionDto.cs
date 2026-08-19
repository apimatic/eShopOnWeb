using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.Billing;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionDto
{
    public long Id { get; set; }
    public string ProductHandle { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public long PriceInCents { get; set; }
    public decimal Price { get; set; }
    public string State { get; set; } = string.Empty;
    public DateTimeOffset? NextBillingAt { get; set; }

    public static SubscriptionDto From(CustomerSubscription subscription)
    {
        return new SubscriptionDto
        {
            Id = subscription.Id,
            ProductHandle = subscription.ProductHandle,
            ProductName = subscription.ProductName,
            PriceInCents = subscription.PriceInCents,
            Price = subscription.Price,
            State = subscription.State,
            NextBillingAt = subscription.NextBillingAt
        };
    }
}
