namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>
    /// The API handle of the subscription plan to subscribe to — one of the handles
    /// returned by GET /api/subscription-plans.
    /// </summary>
    public string? ProductHandle { get; init; }
}