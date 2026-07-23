namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscribeRequest : BaseRequest
{
    /// <summary>The plan's durable handle, e.g. "eshop-pro".</summary>
    public string? PlanHandle { get; set; }

    /// <summary>
    /// Whose subscription to create. Ignored for non-administrators, who always act on their own
    /// account.
    /// </summary>
    public string? UserReference { get; set; }
}
