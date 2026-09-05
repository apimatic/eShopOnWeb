namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>
    /// The Maxio plan (product) handle to subscribe to, e.g. one returned by
    /// GET /api/subscription-plans.
    /// </summary>
    public string PlanHandle { get; set; } = string.Empty;

    /// <summary>
    /// Always overwritten from the caller's JWT identity before handling - never trust a
    /// client-supplied value for this.
    /// </summary>
    public string BuyerEmail { get; set; } = string.Empty;
}
