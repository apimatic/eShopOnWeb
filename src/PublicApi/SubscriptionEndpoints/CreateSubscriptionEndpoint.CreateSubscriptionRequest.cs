namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>
    /// Handle of the plan to subscribe to, as returned by <c>GET /api/subscription-plans</c>.
    /// </summary>
    public string PlanHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional given name to record on the billing account. Defaults to a name derived from the
    /// authenticated shopper's email address.
    /// </summary>
    public string? FirstName { get; set; }

    /// <summary>
    /// Optional family name to record on the billing account. Defaults to a name derived from the
    /// authenticated shopper's email address.
    /// </summary>
    public string? LastName { get; set; }
}
