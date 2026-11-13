namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>Handle of the plan to subscribe to — one returned by <c>GET /api/subscription-plans</c>.</summary>
    public string? PlanHandle { get; set; }

    /// <summary>Optional; used only when the billing account is first created. Defaults from the e-mail address.</summary>
    public string? FirstName { get; set; }

    /// <summary>Optional; used only when the billing account is first created.</summary>
    public string? LastName { get; set; }
}
