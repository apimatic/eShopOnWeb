using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class LifecycleResponse : BaseResponse
{
    public LifecycleResponse(Guid correlationId) : base(correlationId) { }
    public LifecycleResponse() { }

    public BillingSubscription? Subscription { get; set; }
}
