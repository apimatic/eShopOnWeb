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
    /// The state of this account's subscribe enrollment: Pending (a subscribe is running), Active, or Unknown
    /// (the billing system has not confirmed a subscribe yet); null when the account never subscribed here.
    /// </summary>
    public string? EnrollmentStatus { get; set; }

    /// <summary>True when the billing system's list was missing a subscription this store recorded; it was added.</summary>
    public bool Truncated { get; set; }
}
