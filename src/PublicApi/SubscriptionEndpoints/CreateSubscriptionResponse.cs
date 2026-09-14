using System;
using Microsoft.eShopWeb.PublicApi;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionResponse : BaseResponse
{
    public CreateSubscriptionResponse(Guid correlationId) : base(correlationId)
    {
    }

    public CreateSubscriptionResponse()
    {
    }

    /// <summary>True when a new subscription was created; false when an equivalent live subscription already existed.</summary>
    public bool CreatedNew { get; set; }

    public SubscriptionSummaryDto Subscription { get; set; } = new();
}
