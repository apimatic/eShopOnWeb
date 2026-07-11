namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class PreviewPlanChangeBody
{
    public string TargetProductHandle { get; set; } = string.Empty;
    public bool Immediate { get; set; } = true;
}
