namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionResponse : BaseResponse
{
    public SubscriptionStatusDto Subscription { get; set; } = new SubscriptionStatusDto();
}
