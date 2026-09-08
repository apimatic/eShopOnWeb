using System;
using Microsoft.eShopWeb.PublicApi.Subscriptions.Models;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions.Endpoints;

public class SubscribeResponse : BaseResponse
{
    public SubscribeResponse(Guid correlationId) : base(correlationId)
    {
    }

    /// <summary>True when a new Maxio subscription was created; false when an existing subscription was returned (idempotent repeat).</summary>
    public bool CreatedNow { get; set; }

    public SubscriptionDto Subscription { get; set; } = new();
}
