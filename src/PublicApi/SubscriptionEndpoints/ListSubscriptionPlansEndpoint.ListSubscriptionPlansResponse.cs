using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListSubscriptionPlansResponse : BaseResponse
{
    public List<SubscriptionPlanDto> SubscriptionPlans { get; set; } = new();
}
