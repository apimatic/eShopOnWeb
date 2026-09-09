namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionResponse : BaseResponse
{
    public CreateSubscriptionResponse(Guid correlationId) : base(correlationId)
    {
    }

    public CreateSubscriptionResponse()
    {
    }

    /// <summary>True when a new subscription was created; false when the user was already subscribed (idempotent replay).</summary>
    public bool Created { get; set; }

    public UserSubscriptionDto? Subscription { get; set; }
}
