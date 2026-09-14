using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionPlansListResponse : BaseResponse
{
    public SubscriptionPlansListResponse(Guid correlationId)
        : base(correlationId)
    {
    }

    public SubscriptionPlansListResponse()
    {
    }

    public string? ProductFamilyName { get; set; }

    public string? ProductFamilyHandle { get; set; }

    public List<SubscriptionPlanDto> Plans { get; set; } = new();
}
