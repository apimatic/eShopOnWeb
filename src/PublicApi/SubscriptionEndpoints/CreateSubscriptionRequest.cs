namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class CreateSubscriptionRequest
{
    public string ProductHandle { get; init; } = string.Empty;
}
