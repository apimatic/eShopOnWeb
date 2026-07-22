namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class PlanChangeRequest : BaseRequest
{
    /// <summary>Handle of the plan to move to, e.g. <c>basic-plan</c>.</summary>
    public string TargetPlanHandle { get; set; }

    /// <summary><c>Immediately</c> (prorated) or <c>AtNextRenewal</c> (not prorated). Defaults to immediate.</summary>
    public string? Timing { get; set; }

    /// <summary>The fingerprint returned by the preview route. Required on commit, ignored on preview.</summary>
    public string? PreviewFingerprint { get; set; }

    /// <summary>Set from the caller's token.</summary>
    public string UserReference { get; set; }
}
