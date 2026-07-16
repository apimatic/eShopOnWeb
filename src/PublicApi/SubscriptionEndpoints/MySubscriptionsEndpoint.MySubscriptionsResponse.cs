using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class MySubscriptionsResponse : BaseResponse
{
    public MySubscriptionsResponse(Guid correlationId) : base(correlationId) { }
    public MySubscriptionsResponse() { }

    public List<BillingSubscription> Subscriptions { get; set; } = new();
}
