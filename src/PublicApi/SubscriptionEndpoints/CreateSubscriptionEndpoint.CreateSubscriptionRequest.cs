namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest : BaseRequest
{
    public string PlanHandle { get; set; } = string.Empty;

    /// <summary>
    /// Populated from the authenticated caller's identity (JWT) - never trust a client-supplied value here.
    /// </summary>
    public string UserReference { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;
}
