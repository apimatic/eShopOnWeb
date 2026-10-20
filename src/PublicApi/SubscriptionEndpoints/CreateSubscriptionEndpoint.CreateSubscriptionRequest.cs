namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>Handle of a plan returned by <c>GET /api/subscription-plans</c>.</summary>
    public string? ProductHandle { get; set; }
}
