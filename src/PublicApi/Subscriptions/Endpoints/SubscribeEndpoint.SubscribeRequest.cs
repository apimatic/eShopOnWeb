namespace Microsoft.eShopWeb.PublicApi.Subscriptions.Endpoints;

public class SubscribeRequest : BaseRequest
{
    /// <summary>
    /// The handle of the plan to subscribe to. Values come from
    /// GET /api/subscription-plans (e.g. <c>eshop-pro</c>).
    /// </summary>
    public string? PlanHandle { get; set; }

    /// <summary>Optional billing first name. When omitted, one is derived from the shopper's email.</summary>
    public string? FirstName { get; set; }

    /// <summary>Optional billing last name. When omitted, one is derived from the shopper's email.</summary>
    public string? LastName { get; set; }
}
