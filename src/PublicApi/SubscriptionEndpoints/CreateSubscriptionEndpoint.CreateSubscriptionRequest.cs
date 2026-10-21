namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>Handle of the plan to subscribe to, from GET /api/subscription-plans.</summary>
    public string PlanHandle { get; set; } = string.Empty;

    /// <summary>Optional name for the billing account; defaults from the account email when absent.</summary>
    public string? FirstName { get; set; }

    public string? LastName { get; set; }
}
