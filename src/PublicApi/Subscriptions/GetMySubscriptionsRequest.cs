namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public class GetMySubscriptionsRequest : BaseRequest
{
    public string UserId { get; set; } = string.Empty;
}
