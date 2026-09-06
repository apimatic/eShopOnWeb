using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionPlansListResponse : BaseResponse
{
    public SubscriptionPlansListResponse() : base(Guid.NewGuid())
    {
    }

    public List<SubscriptionPlanDto> Plans { get; set; } = new();
}
