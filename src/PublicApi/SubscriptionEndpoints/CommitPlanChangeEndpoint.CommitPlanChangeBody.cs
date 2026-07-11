namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CommitPlanChangeBody
{
    public string TargetProductHandle { get; set; } = string.Empty;
    public bool Immediate { get; set; } = true;
    public string CommitToken { get; set; } = string.Empty;
}
