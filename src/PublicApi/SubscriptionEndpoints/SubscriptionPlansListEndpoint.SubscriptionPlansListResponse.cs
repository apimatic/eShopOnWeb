using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionPlansListResponse : BaseResponse
{
    public SubscriptionPlansListResponse(Guid correlationId) : base(correlationId)
    {
    }

    public SubscriptionPlansListResponse()
    {
    }

    public System.Collections.Generic.List<SubscriptionPlanDto> Plans { get; set; } = new();
}
