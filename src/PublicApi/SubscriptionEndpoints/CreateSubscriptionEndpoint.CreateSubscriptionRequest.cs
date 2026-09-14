namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>Handle of the plan to subscribe to (see GET /api/subscription-plans).</summary>
    public string PlanHandle { get; set; } = string.Empty;

    /// <summary>Optional billing first name for the Maxio customer record. Defaults from the account email.</summary>
    public string? FirstName { get; set; }

    /// <summary>Optional billing last name for the Maxio customer record. Defaults from the account email.</summary>
    public string? LastName { get; set; }

    /// <summary>Populated from the caller's JWT - not settable by the client.</summary>
    public string UserEmail { get; set; } = string.Empty;
}
