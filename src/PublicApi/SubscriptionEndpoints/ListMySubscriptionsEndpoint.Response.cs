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

    /// <summary>The authenticated caller's identity (as used for the Maxio customer reference).</summary>
    public string Reference { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;

    public List<CustomerSubscriptionDto> Subscriptions { get; set; } = new();
}
