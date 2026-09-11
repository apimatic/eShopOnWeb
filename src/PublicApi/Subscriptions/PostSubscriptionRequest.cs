namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public class PostSubscriptionRequest : BaseRequest
{
    public string ProductHandle { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
}
