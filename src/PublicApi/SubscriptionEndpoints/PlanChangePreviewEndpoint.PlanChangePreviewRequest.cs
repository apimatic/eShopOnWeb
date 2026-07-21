namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class PlanChangePreviewRequest : BaseRequest
{
    public int SubscriptionId { get; set; }
    public string TargetPlanHandle { get; set; } = string.Empty;
    public bool ApplyNow { get; set; }

    /// <summary>Set by the route handler from the authenticated caller's identity — never bound from the request body.</summary>
    public string UserName { get; set; } = string.Empty;

    /// <summary>Set by the route handler from the authenticated caller's role — never bound from the request body.</summary>
    public bool IsAdministrator { get; set; }
}
