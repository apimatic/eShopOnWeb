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

    public List<SubscriptionDto> Subscriptions { get; set; } = new List<SubscriptionDto>();

    /// <summary>
    /// Set when a subscribe request for this shopper is still <c>pending</c> or <c>unconfirmed</c>
    /// (the provider has not confirmed it yet); null otherwise.
    /// </summary>
    public string? EnrollmentStatus { get; set; }
}
