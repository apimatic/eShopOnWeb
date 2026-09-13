namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionEndpointRequest : BaseRequest
{
    /// <summary>
    /// The product handle to subscribe to (e.g., "eshop-pro", "basic-plan").
    /// </summary>
    public string ProductHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional user email. If not provided, extracted from JWT claims.
    /// </summary>
    public string? UserEmail { get; set; }
}
