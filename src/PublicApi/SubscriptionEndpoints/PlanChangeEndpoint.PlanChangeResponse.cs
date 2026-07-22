using System;
using Microsoft.eShopWeb.ApplicationCore.Models.Billing;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class PlanChangeResponse : BaseResponse
{
    public PlanChangeResponse(Guid correlationId) : base(correlationId)
    {
    }

    public PlanChangeResponse()
    {
    }

    public string OldPlanHandle { get; set; } = string.Empty;
    public string NewPlanHandle { get; set; } = string.Empty;
    public PlanChangeTiming Timing { get; set; }
    public DateTimeOffset? EffectiveAt { get; set; }
    public SubscriptionDto Subscription { get; set; } = new();
}
