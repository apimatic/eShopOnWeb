using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class PreviewPlanChangeResponse : BaseResponse
{
    public PreviewPlanChangeResponse(Guid correlationId) : base(correlationId)
    {
    }

    public PreviewPlanChangeResponse()
    {
    }

    public string CurrentProductHandle { get; set; } = string.Empty;
    public string TargetProductHandle { get; set; } = string.Empty;
    public bool ApplyImmediately { get; set; }
    public long? ProratedAdjustmentInCents { get; set; }
    public long? ChargeInCents { get; set; }
    public long? PaymentDueInCents { get; set; }
    public long? CreditAppliedInCents { get; set; }

    /// <summary>Pass this back verbatim to CommitPlanChange to confirm the previewed change is still current.</summary>
    public string StalenessToken { get; set; } = string.Empty;
}
