using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscribeResponse : BaseResponse
{
    public SubscribeResponse(Guid correlationId) : base(correlationId) { }
    public SubscribeResponse() { }

    public BillingSubscription? Subscription { get; set; }
}
