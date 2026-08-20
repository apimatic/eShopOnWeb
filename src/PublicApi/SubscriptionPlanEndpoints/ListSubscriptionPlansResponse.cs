using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionPlanEndpoints;

public sealed class ListSubscriptionPlansResponse
{
    public List<SubscriptionPlanDto> Plans { get; init; } = new();
}
