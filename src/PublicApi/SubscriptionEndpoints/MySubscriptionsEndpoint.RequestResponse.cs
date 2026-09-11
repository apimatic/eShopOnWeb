using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class MySubscriptionsRequest
{
    public Guid CorrelationId() => Guid.NewGuid();
}

public class MySubscriptionsResponse : BaseResponse
{
    public MySubscriptionsResponse(Guid correlationId) : base(correlationId) { }
    public List<MySubscriptionResponse> Subscriptions { get; set; } = new();
}

public class MySubscriptionResponse
{
    public int Id { get; set; }
    public string State { get; set; } = string.Empty;
    public string ProductHandle { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public string NextBillingAt { get; set; } = string.Empty;
    public decimal Price { get; set; }
}
