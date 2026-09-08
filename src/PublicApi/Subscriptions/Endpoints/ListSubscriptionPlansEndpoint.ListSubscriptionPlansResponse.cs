using System.Collections.Generic;
using Microsoft.eShopWeb.PublicApi.Subscriptions.Models;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions.Endpoints;

public class ListSubscriptionPlansResponse : BaseResponse
{
    public List<SubscriptionPlanDto> Plans { get; set; } = new();
}
