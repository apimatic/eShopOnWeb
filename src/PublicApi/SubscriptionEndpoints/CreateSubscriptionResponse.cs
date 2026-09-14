using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Result of <c>POST /api/subscriptions</c>. <see cref="Created"/> distinguishes a brand-new
/// subscription (HTTP 201) from an idempotent replay that returned the already-existing one (HTTP 200).
/// </summary>
public class CreateSubscriptionResponse : BaseResponse
{
    public CreateSubscriptionResponse(Guid correlationId) : base(correlationId)
    {
    }

    public CreateSubscriptionResponse()
    {
    }

    public SubscriptionDto? Subscription { get; set; }

    /// <summary>True when Maxio created a new subscription; false when the user already had one for the plan.</summary>
    public bool Created { get; set; }
}
