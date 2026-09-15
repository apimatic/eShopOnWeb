namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>
    /// Maxio product handle to subscribe to (from GET /api/subscription-plans).
    /// </summary>
    public string ProductHandle { get; set; } = string.Empty;
}
