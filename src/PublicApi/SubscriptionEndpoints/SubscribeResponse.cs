using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscribeResponse : BaseResponse
{
    public SubscribeResponse(Guid correlationId) : base(correlationId)
    {
    }

    public SubscribeResponse()
    {
    }

    public CustomerSubscriptionDto? Subscription { get; set; }

    /// <summary>
    /// True when the subscriber was already enrolled in this plan and the existing
    /// subscription was returned (idempotent replay) rather than a new one created.
    /// </summary>
    public bool AlreadyExisted { get; set; }

    public string Message { get; set; } = string.Empty;
}
