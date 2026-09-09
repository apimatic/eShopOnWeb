namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionResponse : BaseResponse
{
    public SubscriptionDto? Subscription { get; set; }

    /// <summary>
    /// True when the subscription already existed (idempotent replay of the same request).
    /// </summary>
    public bool AlreadySubscribed { get; set; }

    public CreateSubscriptionResponse(System.Guid correlationId) : base(correlationId) { }
    public CreateSubscriptionResponse() { }
}
