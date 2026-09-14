namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListMySubscriptionsRequest : BaseRequest
{
    /// <summary>Populated from the caller's JWT - not settable by the client.</summary>
    public string UserEmail { get; set; } = string.Empty;
}
