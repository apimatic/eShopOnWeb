using System.Collections.Generic;
using Microsoft.eShopWeb.PublicApi;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionPlanListResponse : BaseResponse
{
    public SubscriptionPlanListResponse() { }
    public SubscriptionPlanListResponse(System.Guid correlationId) : base(correlationId) { }
    public List<SubscriptionPlanDto> Plans { get; set; } = new();
}
