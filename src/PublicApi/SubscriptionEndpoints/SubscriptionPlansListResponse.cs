using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.PublicApi;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionPlansListResponse : BaseResponse
{
    public List<SubscriptionPlanDto> Plans { get; set; } = new();

    public SubscriptionPlansListResponse(Guid correlationId) : base(correlationId) { }

    public SubscriptionPlansListResponse() { }
}
