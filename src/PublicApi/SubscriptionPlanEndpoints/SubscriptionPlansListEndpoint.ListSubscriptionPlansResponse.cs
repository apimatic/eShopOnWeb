using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionPlanEndpoints;

/// <summary>Response for GET api/subscription-plans.</summary>
public class ListSubscriptionPlansResponse : BaseResponse
{
    public ListSubscriptionPlansResponse()
    {
    }

    public ListSubscriptionPlansResponse(System.Guid correlationId) : base(correlationId)
    {
    }

    public List<SubscriptionPlanDto> SubscriptionPlans { get; set; } = new List<SubscriptionPlanDto>();
}
