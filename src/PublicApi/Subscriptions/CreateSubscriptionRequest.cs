namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class CreateSubscriptionRequest : BaseRequest
{
    public string ProductHandle { get; set; } = string.Empty;
}
