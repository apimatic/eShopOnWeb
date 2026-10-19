namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>Handle of a plan returned by <c>GET /api/subscription-plans</c>.</summary>
    public string PlanHandle { get; set; } = string.Empty;

    /// <summary>Optional billing first name; defaults to the local part of the account e-mail.</summary>
    public string? FirstName { get; set; }

    /// <summary>Optional billing last name.</summary>
    public string? LastName { get; set; }
}
