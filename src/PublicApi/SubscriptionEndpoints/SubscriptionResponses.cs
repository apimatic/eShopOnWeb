using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListSubscriptionPlansResponse : BaseResponse
{
    public ListSubscriptionPlansResponse(Guid correlationId) : base(correlationId)
    {
    }

    public ListSubscriptionPlansResponse()
    {
    }

    public List<SubscriptionPlanDto> SubscriptionPlans { get; set; } = new();
}

public class ListMySubscriptionsResponse : BaseResponse
{
    public ListMySubscriptionsResponse(Guid correlationId) : base(correlationId)
    {
    }

    public ListMySubscriptionsResponse()
    {
    }

    public List<SubscriptionDto> Subscriptions { get; set; } = new();
}

public class CreateSubscriptionApiRequest : BaseRequest
{
    public string ProductHandle { get; set; } = string.Empty;
}

public class CreateSubscriptionApiResponse : BaseResponse
{
    public CreateSubscriptionApiResponse(Guid correlationId) : base(correlationId)
    {
    }

    public CreateSubscriptionApiResponse()
    {
    }

    public SubscriptionDto? Subscription { get; set; }
    public bool AlreadySubscribed { get; set; }
}
