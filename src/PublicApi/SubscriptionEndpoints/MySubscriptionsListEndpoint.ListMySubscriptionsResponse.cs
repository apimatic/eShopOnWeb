using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListMySubscriptionsResponse : BaseResponse
{
    public ListMySubscriptionsResponse(Guid correlationId) : base(correlationId)
    {
    }

    public ListMySubscriptionsResponse()
    {
    }

    /// <summary>The Maxio customer reference (the eShopOnWeb user identity).</summary>
    public string CustomerReference { get; set; } = string.Empty;

    public List<CustomerSubscriptionDto> Subscriptions { get; set; } = new();
}
