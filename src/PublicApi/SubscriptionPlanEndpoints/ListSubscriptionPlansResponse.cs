using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionPlanEndpoints;

public class ListSubscriptionPlansResponse : BaseResponse
{
    public List<SubscriptionPlanDto> SubscriptionPlans { get; set; } = new();
}
