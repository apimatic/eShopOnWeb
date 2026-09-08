namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Response after subscribing the signed-in shopper to a plan.
/// </summary>
public class CreateSubscriptionResponse : BaseResponse
{
    public CreateSubscriptionResponse(System.Guid correlationId) : base(correlationId) { }

    public SubscriptionDto? Subscription { get; set; }

    /// <summary>
    /// True when the user was already subscribed to the plan and no new subscription was created.
    /// </summary>
    public bool AlreadySubscribed { get; set; }
}
