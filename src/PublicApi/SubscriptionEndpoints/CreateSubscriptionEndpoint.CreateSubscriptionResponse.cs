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

    /// <summary>The active subscription (plan, price, state, next billing date).</summary>
    public CustomerSubscriptionDto Subscription { get; set; } = new();

    /// <summary>The Maxio customer id backing the eShopOnWeb user.</summary>
    public int CustomerId { get; set; }

    /// <summary>The Maxio customer reference (the eShopOnWeb user identity).</summary>
    public string CustomerReference { get; set; } = string.Empty;

    /// <summary>True when the user was already subscribed to this plan and the existing subscription was returned.</summary>
    public bool AlreadyExisted { get; set; }
}
