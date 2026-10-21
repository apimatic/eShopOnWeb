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

    /// <summary>Signups of ours that are not settled yet (their outcome is still being confirmed).</summary>
    public List<PendingSignupDto> PendingSignups { get; set; } = new List<PendingSignupDto>();

    /// <summary>False when the subscription list was longer than the page ceiling and was cut short.</summary>
    public bool IsComplete { get; set; } = true;
}
