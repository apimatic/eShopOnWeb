namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionResponse : BaseResponse
{
    public CreateSubscriptionResponse()
    {
    }

    public CreateSubscriptionResponse(System.Guid correlationId) : base(correlationId)
    {
    }

    public SubscriptionDto? Subscription { get; set; }
}
