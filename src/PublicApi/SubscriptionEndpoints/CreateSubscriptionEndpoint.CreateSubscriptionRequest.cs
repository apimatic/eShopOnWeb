namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>The API handle of the plan to subscribe to (e.g. "eshop-pro").</summary>
    public string PlanHandle { get; set; } = string.Empty;

    /// <summary>Set from the caller's JWT identity; not part of the request body.</summary>
    public string? CustomerReference { get; set; }
}
