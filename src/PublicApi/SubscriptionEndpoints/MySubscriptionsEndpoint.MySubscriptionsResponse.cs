using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class MySubscriptionsResponse : BaseResponse
{
    public MySubscriptionsResponse(Guid correlationId) : base(correlationId)
    {
    }

    public MySubscriptionsResponse()
    {
    }

    /// <summary>The reference this account is known by in the billing system.</summary>
    public string CustomerReference { get; set; } = string.Empty;

    /// <summary>
    /// Identifier of the billing customer, or null when this account has never subscribed.
    /// </summary>
    public long? CustomerId { get; set; }

    /// <summary>Every subscription on the account, newest first, including ended ones.</summary>
    public List<SubscriptionDto> Subscriptions { get; set; } = new List<SubscriptionDto>();
}
