using System;
using Microsoft.eShopWeb.ApplicationCore.Billing;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionDto
{
    public int Id { get; set; }
    public string State { get; set; } = string.Empty;
    public string? ProductHandle { get; set; }
    public string? ProductName { get; set; }
    public long PriceInCents { get; set; }
    public decimal Price { get; set; }
    public DateTimeOffset? NextBillingAt { get; set; }

    public static SubscriptionDto From(CustomerSubscription subscription) => new()
    {
        Id = subscription.Id,
        State = subscription.State,
        ProductHandle = subscription.ProductHandle,
        ProductName = subscription.ProductName,
        PriceInCents = subscription.PriceInCents,
        Price = subscription.PriceInCents / 100m,
        NextBillingAt = subscription.NextBillingAt
    };
}

public class CreateSubscriptionResponse : BaseResponse
{
    public CreateSubscriptionResponse(Guid correlationId) : base(correlationId)
    {
    }

    public CreateSubscriptionResponse()
    {
    }

    public SubscriptionDto? Subscription { get; set; }
}

public class ListMySubscriptionsResponse : BaseResponse
{
    public ListMySubscriptionsResponse(Guid correlationId) : base(correlationId)
    {
    }

    public ListMySubscriptionsResponse()
    {
    }

    public System.Collections.Generic.List<SubscriptionDto> Subscriptions { get; set; } = new();
}
