namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Request to subscribe the authenticated user to a plan.
/// </summary>
public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>API handle of the plan to subscribe to (see GET /api/subscription-plans).</summary>
    public string PlanHandle { get; set; } = string.Empty;

    /// <summary>Optional billing first name for the Maxio customer record.</summary>
    public string? FirstName { get; set; }

    /// <summary>Optional billing last name for the Maxio customer record.</summary>
    public string? LastName { get; set; }
}
