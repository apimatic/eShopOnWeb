namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionResponse : BaseResponse
{
    public CreateSubscriptionResponse() : base()
    {
    }

    public CreateSubscriptionResponse(System.Guid correlationId) : base(correlationId)
    {
    }

    /// <summary>
    /// The subscription in Maxio. When the caller is already subscribed to the requested
    /// plan the existing subscription is returned unchanged (the call is idempotent).
    /// </summary>
    public SubscriptionDto Subscription { get; set; } = new();
}
