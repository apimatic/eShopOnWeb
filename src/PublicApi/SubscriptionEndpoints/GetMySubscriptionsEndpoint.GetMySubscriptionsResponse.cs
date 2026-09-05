using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class GetMySubscriptionsResponse : BaseResponse
{
    public GetMySubscriptionsResponse(Guid correlationId) : base(correlationId) { }
    public GetMySubscriptionsResponse() { }

    public List<SubscriptionDto> Subscriptions { get; set; } = new();
}

public class SubscriptionDto
{
    public int Id { get; set; }
    public string State { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public string ProductHandle { get; set; } = string.Empty;
    public decimal PricePerMonth { get; set; }
    public DateTime CurrentPeriodStartsAt { get; set; }
    public DateTime CurrentPeriodEndsAt { get; set; }
    public DateTime NextBillingDate { get; set; }
}
