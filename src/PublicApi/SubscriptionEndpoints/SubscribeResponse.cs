using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscribeResponse : BaseResponse
{
    public SubscribeResponse(Guid correlationId) : base(correlationId)
    {
    }

    public SubscribeResponse()
    {
    }

    /// <summary>The active subscription for the shopper on the requested plan.</summary>
    public SubscriptionDto Subscription { get; set; } = new();

    /// <summary>The Maxio customer id for the shopper.</summary>
    public int CustomerId { get; set; }

    /// <summary>
    /// True when an equivalent active subscription already existed and was returned instead of creating a
    /// duplicate (e.g. a double-clicked subscribe).
    /// </summary>
    public bool AlreadyExisted { get; set; }

    /// <summary>True when a new Maxio customer had to be created for the shopper during this call.</summary>
    public bool CustomerCreated { get; set; }

    /// <summary>A human-readable summary of the outcome.</summary>
    public string Message { get; set; } = string.Empty;
}
