using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionPlansRequest
{
    public Guid CorrelationId() => Guid.NewGuid();
}

public class SubscriptionPlansResponse : BaseResponse
{
    public SubscriptionPlansResponse(Guid correlationId) : base(correlationId) { }
    public List<PlanResponse> Plans { get; set; } = new();
}

public class PlanResponse
{
    public int Id { get; set; }
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string Currency { get; set; } = string.Empty;
}
