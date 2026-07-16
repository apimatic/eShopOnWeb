using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.SubscriptionAggregate;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CommitPlanChangeResponse : BaseResponse
{
    public CommitPlanChangeResponse(Guid correlationId) : base(correlationId) { }
    public CommitPlanChangeResponse() { }

    public BillingSubscription? Subscription { get; set; }
}
