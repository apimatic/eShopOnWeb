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

    /// <summary>The caller's subscriptions, newest first.</summary>
    public List<SubscriptionDto> Subscriptions { get; set; } = new List<SubscriptionDto>();

    /// <summary>
    /// The reference that identifies this eShopOnWeb user in the billing system. Always present,
    /// even before the user has any subscription.
    /// </summary>
    public string CustomerReference { get; set; } = string.Empty;

    /// <summary>The billing-system customer id, or null when no customer exists for the caller yet.</summary>
    public int? CustomerId { get; set; }
}
