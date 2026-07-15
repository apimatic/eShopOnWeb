namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class PreviewPlanChangeRequest : BaseRequest
{
    public int SubscriptionId { get; set; }
    public string TargetProductHandle { get; set; } = string.Empty;

    /// <summary>"Now" (prorated) or "AtNextRenewal" (no proration) — see <see cref="ApplicationCore.Entities.SubscriptionAggregate.PlanChangeTiming"/>.</summary>
    public string Timing { get; set; } = string.Empty;

    public string? OwnerUserId { get; set; }
}
