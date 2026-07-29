using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionResponse : BaseResponse
{
    public CreateSubscriptionResponse(Guid correlationId) : base(correlationId)
    {
    }

    public CreateSubscriptionResponse()
    {
    }

    /// <summary>The active subscription — either newly created or the pre-existing one (idempotency).</summary>
    public CustomerSubscriptionDto? Subscription { get; set; }

    /// <summary>True when the subscription already existed and was returned instead of creating a duplicate.</summary>
    public bool AlreadyExisted { get; set; }

    /// <summary>Human-readable confirmation of the outcome.</summary>
    public string Message { get; set; } = string.Empty;
}
