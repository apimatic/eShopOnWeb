using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Response confirming a subscription enrollment.
/// </summary>
public class CreateSubscriptionResponse : BaseResponse
{
    public CreateSubscriptionResponse(Guid correlationId) : base(correlationId)
    {
    }

    public CreateSubscriptionResponse()
    {
    }

    /// <summary>The subscription recorded by Maxio (the billing system of record).</summary>
    public SubscriptionDto Subscription { get; set; } = new();

    /// <summary>
    /// False when the call was recognized as a duplicate of an earlier enrollment
    /// (idempotent replay); true when the subscription was created by this call.
    /// </summary>
    public bool NewlyCreated { get; set; }
}
