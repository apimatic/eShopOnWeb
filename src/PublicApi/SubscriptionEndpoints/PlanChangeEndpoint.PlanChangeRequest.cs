namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class PlanChangeRequest : BaseRequest
{
    public int SubscriptionId { get; set; }
    public string TargetProductHandle { get; set; } = string.Empty;

    /// <summary>true = apply now with proration; false = apply at next renewal without proration.</summary>
    public bool ApplyNow { get; set; }
}
