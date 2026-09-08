using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>
    /// Handle of the plan to subscribe to (as returned by GET /api/subscription-plans),
    /// e.g. "eshop-pro".
    /// </summary>
    public string ProductHandle { get; init; } = string.Empty;
}

public class CreateSubscriptionResponse : BaseResponse
{
    public CreateSubscriptionResponse()
    {
    }

    public CreateSubscriptionResponse(System.Guid correlationId) : base(correlationId)
    {
    }

    public SubscriptionDto Subscription { get; set; } = new SubscriptionDto();

    /// <summary>
    /// False when the call was idempotent — the user was already subscribed to the plan and
    /// the existing subscription is returned.
    /// </summary>
    public bool Created { get; set; }
}

public class SubscriptionPlansResponse : BaseResponse
{
    public SubscriptionPlansResponse()
    {
    }

    public SubscriptionPlansResponse(System.Guid correlationId) : base(correlationId)
    {
    }

    public List<SubscriptionPlanDto> Plans { get; set; } = new List<SubscriptionPlanDto>();
}

public class MySubscriptionsResponse : BaseResponse
{
    public MySubscriptionsResponse()
    {
    }

    public MySubscriptionsResponse(System.Guid correlationId) : base(correlationId)
    {
    }

    public List<SubscriptionDto> Subscriptions { get; set; } = new List<SubscriptionDto>();
}
