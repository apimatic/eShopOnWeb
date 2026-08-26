namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>
    /// API handle of the plan to subscribe to (see GET /api/subscription-plans).
    /// </summary>
    public string ProductHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional display name used when the billing customer is first created. Derived from the
    /// shopper's email address when omitted.
    /// </summary>
    public string? FirstName { get; set; }

    public string? LastName { get; set; }
}
