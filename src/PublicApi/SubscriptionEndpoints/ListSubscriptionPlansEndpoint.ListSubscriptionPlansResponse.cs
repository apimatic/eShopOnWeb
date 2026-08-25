using System.Collections.Generic;
using Microsoft.eShopWeb.PublicApi.Services;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListSubscriptionPlansResponse : BaseResponse
{
    public IReadOnlyList<SubscriptionPlanDto> Plans { get; set; } = new List<SubscriptionPlanDto>();
}
