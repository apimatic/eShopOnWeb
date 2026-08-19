using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionPlanDto
{
    public string Handle { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }
    public decimal Price { get; set; }
    public int Interval { get; set; }
    public string IntervalUnit { get; set; }
    public bool RequiresCreditCard { get; set; }
}

public class ShopperSubscriptionDto
{
    public int Id { get; set; }
    public string ProductHandle { get; set; }
    public string ProductName { get; set; }
    public string State { get; set; }
    public decimal Price { get; set; }
    public System.DateTimeOffset? CurrentPeriodEndsAt { get; set; }
    public System.DateTimeOffset? NextBillingAt { get; set; }
    public string Reference { get; set; }
}

public class ListSubscriptionPlansResponse : BaseResponse
{
    public ListSubscriptionPlansResponse(System.Guid correlationId) : base(correlationId)
    {
    }

    public ListSubscriptionPlansResponse()
    {
    }

    public List<SubscriptionPlanDto> Plans { get; set; } = new();
}

public class ListMySubscriptionsResponse : BaseResponse
{
    public ListMySubscriptionsResponse(System.Guid correlationId) : base(correlationId)
    {
    }

    public ListMySubscriptionsResponse()
    {
    }

    public List<ShopperSubscriptionDto> Subscriptions { get; set; } = new();
}

public class CreateShopperSubscriptionRequest : BaseRequest
{
    public string ProductHandle { get; set; }
}

public class CreateShopperSubscriptionResponse : BaseResponse
{
    public CreateShopperSubscriptionResponse(System.Guid correlationId) : base(correlationId)
    {
    }

    public CreateShopperSubscriptionResponse()
    {
    }

    public ShopperSubscriptionDto Subscription { get; set; }
}
