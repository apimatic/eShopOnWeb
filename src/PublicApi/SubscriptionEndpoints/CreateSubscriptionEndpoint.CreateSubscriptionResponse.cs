namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionResponse : BaseResponse
{
    public CreateSubscriptionResponse(System.Guid correlationId) : base(correlationId)
    {
    }

    public CreateSubscriptionResponse()
    {
    }

    /// <summary>
    /// True when a brand-new subscription was created; false when the shopper was already
    /// subscribed to this plan and the existing subscription was returned (idempotent repeat).
    /// </summary>
    public bool Created { get; set; }

    /// <summary>The Maxio customer id backing this shopper.</summary>
    public long CustomerId { get; set; }

    public SubscriptionDto Subscription { get; set; } = new();
}
