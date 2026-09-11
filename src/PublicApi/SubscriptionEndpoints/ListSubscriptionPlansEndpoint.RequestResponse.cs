using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.PublicApi;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListSubscriptionPlansRequest : BaseRequest
{
    public ListSubscriptionPlansRequest() { }
}

public class ListSubscriptionPlansResponse : BaseResponse
{
    public ListSubscriptionPlansResponse(Guid correlationId) : base(correlationId) { }
    public List<SubscriptionPlanDto> Plans { get; set; } = new();
}
