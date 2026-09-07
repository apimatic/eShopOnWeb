namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Request to create a subscription
/// </summary>
public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>
    /// Handle of the product/plan to subscribe to (e.g., "eshop-pro", "basic-plan")
    /// </summary>
    public string? ProductHandle { get; set; }

    /// <summary>
    /// Optional reference for the subscription (if not provided, one will be generated)
    /// </summary>
    public string? Reference { get; set; }
}
