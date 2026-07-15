using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListPlansResponse : BaseResponse
{
    public List<BillingPlanDto> Plans { get; set; } = new();
}
