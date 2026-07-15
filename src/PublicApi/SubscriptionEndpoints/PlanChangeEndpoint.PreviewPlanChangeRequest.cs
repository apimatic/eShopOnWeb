namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class PreviewPlanChangeRequest : BaseRequest
{
    public int SubscriptionId { get; set; }
    public string TargetPlanHandle { get; set; } = string.Empty;
    public bool ApplyNow { get; set; }

    /// <summary>Overwritten server-side from the authenticated principal — never trust a client-supplied value.</summary>
    public string UserReference { get; set; } = string.Empty;
}
