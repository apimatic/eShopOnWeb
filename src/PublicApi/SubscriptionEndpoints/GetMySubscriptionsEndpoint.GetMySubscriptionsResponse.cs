using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class GetMySubscriptionsResponse : BaseResponse
{
    public GetMySubscriptionsResponse(Guid correlationId) : base(correlationId) { }
    public GetMySubscriptionsResponse() { }

    public List<UserSubscriptionDto> Subscriptions { get; set; } = new();
}
