using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class GetSubscriptionPlansRequest : BaseRequest
{
}

public class GetSubscriptionPlansResponse : BaseResponse
{
    public GetSubscriptionPlansResponse(Guid correlationId) : base(correlationId)
    {
    }

    public GetSubscriptionPlansResponse()
    {
    }

    public string ProductFamilyHandle { get; set; } = string.Empty;
    public List<SubscriptionPlanDto> Plans { get; set; } = new();
    public List<SubscriptionComponentDto> Components { get; set; } = new();
}