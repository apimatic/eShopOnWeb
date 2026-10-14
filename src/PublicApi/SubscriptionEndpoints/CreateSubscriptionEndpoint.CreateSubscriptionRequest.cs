namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>Handle of a plan returned by <c>GET /api/subscription-plans</c>.</summary>
    public string PlanHandle { get; set; } = string.Empty;

    /// <summary>Optional; used only when the billing customer is first created.</summary>
    public string? FirstName { get; set; }

    /// <summary>Optional; used only when the billing customer is first created.</summary>
    public string? LastName { get; set; }
}
