namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionResponse : BaseResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public SubscriptionDto? Subscription { get; set; }
}
