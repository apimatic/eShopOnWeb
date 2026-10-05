using System;
using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Request to subscribe the authenticated user to a plan.
/// </summary>
public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>The Maxio product handle of the plan to subscribe to.</summary>
    [Required]
    public string? PlanHandle { get; set; }
}

/// <summary>
/// Response confirming the subscription (plan, price, state, next billing date).
/// </summary>
public class CreateSubscriptionResponse : BaseResponse
{
    public CreateSubscriptionResponse(Guid correlationId) : base(correlationId)
    {
    }

    public CreateSubscriptionResponse()
    {
    }

    /// <summary>True when the user was already subscribed and no new subscription was created.</summary>
    public bool AlreadySubscribed { get; set; }

    public SubscriptionDto? Subscription { get; set; }
}