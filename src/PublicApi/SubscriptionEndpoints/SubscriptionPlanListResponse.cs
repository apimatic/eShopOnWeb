using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Response listing the available subscription plans.
/// </summary>
public class SubscriptionPlanListResponse : BaseResponse
{
    public SubscriptionPlanListResponse() { }

    public SubscriptionPlanListResponse(System.Guid correlationId) : base(correlationId) { }

    public List<SubscriptionPlanDto> Plans { get; set; } = new List<SubscriptionPlanDto>();
}
