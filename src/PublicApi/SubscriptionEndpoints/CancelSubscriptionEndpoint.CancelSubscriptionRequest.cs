using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Request to cancel one of the authenticated user's subscriptions
/// </summary>
public class CancelSubscriptionRequest : BaseRequest
{
    public int SubscriptionId { get; set; }

    /// <summary>
    /// Populated from the JWT identity by the endpoint; not supplied by callers.
    /// </summary>
    public string Username { get; set; } = string.Empty;
}

/// <summary>
/// Response confirming the cancellation
/// </summary>
public class CancelSubscriptionResponse : BaseResponse
{
    public CancelSubscriptionResponse()
    {
    }

    public CancelSubscriptionResponse(Guid correlationId) : base(correlationId)
    {
    }

    public SubscriptionDto Subscription { get; set; } = new();
}
