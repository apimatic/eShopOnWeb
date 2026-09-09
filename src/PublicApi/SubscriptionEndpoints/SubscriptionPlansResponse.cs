using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionPlansResponse : BaseResponse
{
    public SubscriptionPlansResponse(Guid correlationId) : base(correlationId)
    {
    }

    public SubscriptionPlansResponse()
    {
    }

    public List<SubscriptionPlanDto> Plans { get; set; } = new();
}
