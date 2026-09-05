using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListSubscriptionPlansResponse : BaseResponse
{
    public ListSubscriptionPlansResponse()
    {
    }

    public List<SubscriptionPlanDto> SubscriptionPlans { get; set; } = new();
}
