namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionResponse : BaseResponse
{
    public CreateSubscriptionResponse() { }

    public SubscriptionResultDto Subscription { get; set; } = new SubscriptionResultDto();
}
