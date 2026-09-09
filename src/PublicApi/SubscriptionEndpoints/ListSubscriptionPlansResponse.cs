using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Lists the subscription plans available to shoppers.
/// </summary>
public class ListSubscriptionPlansResponse
{
    public List<SubscriptionPlanDto> SubscriptionPlans { get; } = new();
}
