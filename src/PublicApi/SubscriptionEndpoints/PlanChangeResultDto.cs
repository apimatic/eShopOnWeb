namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// The committed outcome of a plan change.
/// </summary>
public class PlanChangeResultDto
{
    public SubscriptionDto Subscription { get; set; }
    public string? PreviousPlanHandle { get; set; }
    public string? PreviousPlanName { get; set; }
    public string TargetPlanHandle { get; set; }
    public string TargetPlanName { get; set; }
    public string Timing { get; set; }
    public decimal AmountApplied { get; set; }
}
