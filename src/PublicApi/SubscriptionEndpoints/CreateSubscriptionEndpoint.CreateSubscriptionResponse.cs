using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Response confirming the subscription: plan, price, state and next
/// billing date.
/// </summary>
public class CreateSubscriptionResponse : BaseResponse
{
    public CreateSubscriptionResponse(Guid correlationId) : base(correlationId)
    {
    }

    public CreateSubscriptionResponse() : base()
    {
    }

    /// <summary>
    /// True when the caller already held a live subscription to the plan
    /// (the request was an idempotent no-op).
    /// </summary>
    public bool AlreadySubscribed { get; set; }

    public string? Message { get; set; }

    public SubscriptionDto? Subscription { get; set; }
}