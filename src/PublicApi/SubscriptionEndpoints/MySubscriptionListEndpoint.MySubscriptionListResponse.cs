using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class MySubscriptionListResponse : BaseResponse
{
    public MySubscriptionListResponse(Guid correlationId) : base(correlationId)
    {
    }

    public MySubscriptionListResponse()
    {
    }

    /// <summary>The billing customer reference derived from the signed-in account.</summary>
    public string CustomerReference { get; set; }

    public List<SubscriptionDto> Subscriptions { get; set; } = new List<SubscriptionDto>();
}
