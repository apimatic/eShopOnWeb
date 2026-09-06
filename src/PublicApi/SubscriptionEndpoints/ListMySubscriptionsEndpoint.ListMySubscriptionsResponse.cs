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

    /// <summary>The reference that links this shopper to their billing-system customer record.</summary>
    public string CustomerReference { get; set; } = string.Empty;

    /// <summary>Every subscription held by the shopper, newest first.</summary>
    public List<SubscriptionDto> Subscriptions { get; set; } = new();
}
