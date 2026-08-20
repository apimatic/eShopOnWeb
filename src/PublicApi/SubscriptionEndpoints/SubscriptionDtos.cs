using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Billing;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionPlanDto
{
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public long PriceInCents { get; set; }
    public decimal Price { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; } = string.Empty;

    public static SubscriptionPlanDto From(SubscriptionPlan plan)
    {
        return new SubscriptionPlanDto
        {
            Handle = plan.Handle,
            Name = plan.Name,
            Description = plan.Description,
            PriceInCents = plan.PriceInCents,
            Price = plan.PriceInCents / 100m,
            Interval = plan.Interval,
            IntervalUnit = plan.IntervalUnit
        };
    }
}

public class ShopperSubscriptionDto
{
    public int Id { get; set; }
    public string State { get; set; } = string.Empty;
    public string ProductHandle { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public long PriceInCents { get; set; }
    public decimal Price { get; set; }
    public DateTimeOffset? NextBillingAt { get; set; }

    public static ShopperSubscriptionDto From(ShopperSubscription subscription)
    {
        return new ShopperSubscriptionDto
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
}

public class ListSubscriptionPlansResponse : BaseResponse
{
    public ListSubscriptionPlansResponse(Guid correlationId) : base(correlationId)
    {
    }

    public ListSubscriptionPlansResponse()
    {
    }

    public List<SubscriptionPlanDto> Plans { get; set; } = new();
}

public class ListMySubscriptionsResponse : BaseResponse
{
    public ListMySubscriptionsResponse(Guid correlationId) : base(correlationId)
    {
    }

    public ListMySubscriptionsResponse()
    {
    }

    public List<ShopperSubscriptionDto> Subscriptions { get; set; } = new();
}

public class CreateShopperSubscriptionRequest : BaseRequest
{
    /// <summary>
    /// Maxio product handle to subscribe to (for example the Pro plan handle from GET /api/subscription-plans).
    /// </summary>
    public string ProductHandle { get; set; } = string.Empty;
}

public class CreateShopperSubscriptionResponse : BaseResponse
{
    public CreateShopperSubscriptionResponse(Guid correlationId) : base(correlationId)
    {
    }

    public CreateShopperSubscriptionResponse()
    {
    }

    public ShopperSubscriptionDto? Subscription { get; set; }
    public bool Created { get; set; }
}
