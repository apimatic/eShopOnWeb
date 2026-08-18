using System;
using Microsoft.eShopWeb.ApplicationCore.Billing;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionResponse : BaseResponse
{
    public CreateSubscriptionResponse(Guid correlationId) : base(correlationId)
    {
    }

    public CreateSubscriptionResponse()
    {
    }

    public SubscriptionDto Subscription { get; set; } = new();
    public bool AlreadySubscribed { get; set; }

    public static SubscriptionDto From(ShopperSubscription subscription) => new()
    {
        Id = subscription.Id,
        Reference = subscription.Reference,
        State = subscription.State,
        ProductHandle = subscription.ProductHandle,
        ProductName = subscription.ProductName,
        Price = subscription.Price,
        PriceInCents = subscription.PriceInCents,
        NextBillingAt = subscription.NextBillingAt,
        CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
        CreatedAt = subscription.CreatedAt
    };
}
