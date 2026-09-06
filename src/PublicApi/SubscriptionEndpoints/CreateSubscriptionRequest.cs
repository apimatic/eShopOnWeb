namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public record CreateSubscriptionRequest
{
    public string ProductHandle { get; init; } = string.Empty;
}
