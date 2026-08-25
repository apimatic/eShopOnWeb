namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListMySubscriptionsRequest : BaseRequest
{
    /// <summary>The authenticated user's username, populated from the JWT — never from the client.</summary>
    public string Username { get; set; } = string.Empty;
}
