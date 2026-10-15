using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscribeResponse : BaseResponse
{
    public SubscribeResponse(Guid correlationId) : base(correlationId)
    {
    }

    public SubscribeResponse()
    {
    }

    public SubscriptionSummary? Subscription { get; set; }

    /// <summary>True when the user already held this subscription (no new one was created).</summary>
    public bool AlreadySubscribed { get; set; }
}
