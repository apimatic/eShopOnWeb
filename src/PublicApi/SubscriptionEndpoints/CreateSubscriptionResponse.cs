namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionResponse
{
    public SubscriptionDto? Subscription { get; init; }
    public string? ErrorMessage { get; init; }
}
