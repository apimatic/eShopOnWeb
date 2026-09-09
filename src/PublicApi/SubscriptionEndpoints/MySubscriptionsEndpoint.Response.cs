using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.PublicApi;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class MySubscriptionDto
{
    public int SubscriptionId { get; set; }

    public string PlanHandle { get; set; } = string.Empty;

    public string PlanName { get; set; } = string.Empty;

    public long PriceInCents { get; set; }

    public string Price { get; set; } = string.Empty;

    public string State { get; set; } = string.Empty;

    public DateTime? NextBillingAt { get; set; }

    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// True when state/price/next-billing were refreshed live from Maxio during this request.
    /// </summary>
    public bool Live { get; set; }
}

public class MySubscriptionsResponse : BaseResponse
{
    public List<MySubscriptionDto> Subscriptions { get; set; } = new();

    public MySubscriptionsResponse(Guid correlationId) : base(correlationId) { }

    public MySubscriptionsResponse() { }
}
