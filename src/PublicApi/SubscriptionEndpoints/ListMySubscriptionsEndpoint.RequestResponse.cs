using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.PublicApi;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class MySubscriptionDto
{
    public int Id { get; set; }
    public string PlanHandle { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public decimal PriceMonthly { get; set; }
    public DateTime? NextBillingDate { get; set; }
}

public class ListMySubscriptionsRequest : BaseRequest { }

public class ListMySubscriptionsResponse : BaseResponse
{
    public ListMySubscriptionsResponse(Guid correlationId) : base(correlationId) { }
    public List<MySubscriptionDto> Subscriptions { get; set; } = new();
}
