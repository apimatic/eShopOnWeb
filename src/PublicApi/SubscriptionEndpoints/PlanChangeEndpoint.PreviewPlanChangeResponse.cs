using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class PreviewPlanChangeResponse : BaseResponse
{
    public PreviewPlanChangeResponse(Guid correlationId) : base(correlationId) { }
    public PreviewPlanChangeResponse() { }

    public PlanChangePreview? Preview { get; set; }
}
