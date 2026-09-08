using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.PublicApi;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionPlanListResponse : BaseResponse
{
    public SubscriptionPlanListResponse(Guid correlationId) : base(correlationId) { }

    public SubscriptionPlanListResponse()
    {
    }

    public IReadOnlyList<SubscriptionPlanDto> Plans { get; set; } = new List<SubscriptionPlanDto>();

    public string ProductFamilyHandle { get; set; } = string.Empty;
}
