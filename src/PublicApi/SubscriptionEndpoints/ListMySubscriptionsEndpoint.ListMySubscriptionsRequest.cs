namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListMySubscriptionsRequest : BaseRequest
{
    // Populated server-side from the caller's JWT identity in ListMySubscriptionsEndpoint.AddRoute.
    internal string UserId { get; set; } = string.Empty;
}
