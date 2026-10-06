namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribe the authenticated shopper to a plan
/// </summary>
public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>Maxio product handle of the plan, e.g. "eshop-pro".</summary>
    public string? PlanHandle { get; set; }

    /// <summary>Server-assigned from the JWT; ignored if supplied by the caller.</summary>
    public string? ShopperEmail { get; set; }
}
