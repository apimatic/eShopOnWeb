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

    public string TargetPlanHandle { get; set; } = string.Empty;
    public bool Prorated { get; set; }
    public DateTimeOffset EffectiveDate { get; set; }
    public int? ProratedAdjustmentInCents { get; set; }
}
