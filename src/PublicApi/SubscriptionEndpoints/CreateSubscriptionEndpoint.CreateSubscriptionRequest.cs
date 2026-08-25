namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>
    /// Handle of the plan to subscribe to. When omitted, the first plan in the
    /// configured product family is used.
    /// </summary>
    public string? PlanHandle { get; set; }
}
