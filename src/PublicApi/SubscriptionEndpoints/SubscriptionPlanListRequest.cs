using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionPlanListRequest
{
    public Guid CorrelationId { get; } = Guid.NewGuid();
}
