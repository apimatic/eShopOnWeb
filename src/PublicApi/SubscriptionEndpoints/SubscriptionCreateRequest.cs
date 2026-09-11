using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionCreateRequest
{
    public Guid CorrelationId { get; } = Guid.NewGuid();
    public string PlanHandle { get; set; } = "";
}
