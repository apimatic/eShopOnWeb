using System.Collections.Generic;
using Microsoft.eShopWeb.PublicApi.Billing;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionPlanEndpoints;

public class ListSubscriptionPlansResponse : BaseResponse
{
    public List<SubscriptionPlanDto> Plans { get; set; } = new();
}
