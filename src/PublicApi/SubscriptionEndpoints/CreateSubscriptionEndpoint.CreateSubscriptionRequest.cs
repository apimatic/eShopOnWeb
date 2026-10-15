namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>Handle of the plan to subscribe to — one returned by <c>GET /api/subscription-plans</c>.</summary>
    public string? PlanHandle { get; set; }

    /// <summary>Optional billing first name; defaults to the part of the account email before the '@'.</summary>
    public string? FirstName { get; set; }

    /// <summary>Optional billing last name.</summary>
    public string? LastName { get; set; }
}
