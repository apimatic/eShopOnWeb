namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class PreviewPlanChangeRequest : BaseRequest
{
    public int SubscriptionId { get; set; }
    public string TargetPlanHandle { get; set; } = string.Empty;

    /// <summary>true = apply now with proration; false = at next renewal, no proration.</summary>
    public bool ApplyNow { get; set; }

    /// <summary>Set by the endpoint from the authenticated JWT principal — never trusted from client input.</summary>
    public string CustomerReference { get; set; } = string.Empty;
    public bool IsAdmin { get; set; }
}
