using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionRequest
{
    public string ProductHandle { get; set; } = string.Empty;
    public Guid CorrelationId() => Guid.NewGuid();
}

public class CreateSubscriptionResponse : BaseResponse
{
    public CreateSubscriptionResponse(Guid correlationId) : base(correlationId) { }
    public int SubscriptionId { get; set; }
    public string State { get; set; } = string.Empty;
    public string ProductHandle { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public string NextBillingAt { get; set; } = string.Empty;
    public decimal Price { get; set; }
}
