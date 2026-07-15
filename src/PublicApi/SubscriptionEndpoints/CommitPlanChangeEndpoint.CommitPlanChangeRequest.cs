namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CommitPlanChangeRequest : BaseRequest
{
    public int SubscriptionId { get; set; }
    public string TargetProductHandle { get; set; } = string.Empty;
    public bool ApplyImmediately { get; set; }
    public string StalenessToken { get; set; } = string.Empty;

    /// <summary>Server-assigned from the authenticated principal — never bound from client input.</summary>
    public string? UserId { get; set; }
}
