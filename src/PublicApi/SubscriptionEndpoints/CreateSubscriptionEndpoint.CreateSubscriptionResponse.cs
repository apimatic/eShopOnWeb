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

    /// <summary><c>created</c>, <c>already_subscribed</c>, <c>pending</c> or <c>action_required</c>.</summary>
    public string Outcome { get; set; } = string.Empty;

    /// <summary>Our record of this signup; quote it when asking about a pending signup.</summary>
    public string SignupId { get; set; } = string.Empty;

    /// <summary>The subscription (plan, price, state, next billing date); null while pending.</summary>
    public SubscriptionDto? Subscription { get; set; }

    /// <summary>Where the shopper completes a required action (3-D Secure), when the outcome is <c>action_required</c>.</summary>
    public string? ActionLink { get; set; }
}
