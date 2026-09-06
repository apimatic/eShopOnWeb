using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionPlansEndpoints;

public class ListSubscriptionPlansResponse
{
    public List<SubscriptionPlanDto> Plans { get; } = new();
}
