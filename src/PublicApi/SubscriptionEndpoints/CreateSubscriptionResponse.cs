using System;
using Microsoft.eShopWeb.ApplicationCore.SubscriptionBilling;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Response for POST api/subscriptions
/// </summary>
public class CreateSubscriptionResponse : BaseResponse
{
    public CreateSubscriptionResponse(Guid correlationId) : base(correlationId)
    {
    }

    public CreateSubscriptionResponse()
    {
    }

    public SubscriptionDetailsDto Subscription { get; set; } = null!;

    /// <summary>
    /// True when this call created the subscription; false when an already-active
    /// subscription for the same plan was returned (idempotent replay).
    /// </summary>
    public bool Created { get; set; }
}
