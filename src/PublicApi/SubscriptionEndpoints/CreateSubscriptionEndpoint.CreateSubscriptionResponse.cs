using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Result of <c>POST /api/subscriptions</c>.
/// </summary>
public class CreateSubscriptionResponse : BaseResponse
{
    public CreateSubscriptionResponse(Guid correlationId) : base(correlationId)
    {
    }

    public CreateSubscriptionResponse()
    {
    }

    /// <summary>
    /// <c>true</c> when this call enrolled the shopper. <c>false</c> when they were already
    /// subscribed and the existing subscription is being returned instead.
    /// </summary>
    public bool Created { get; set; }

    /// <summary><c>true</c> when this call also created the billing customer for the shopper.</summary>
    public bool CustomerCreated { get; set; }

    /// <summary>The reference that identifies this shopper in the billing system.</summary>
    public string CustomerReference { get; set; } = string.Empty;

    /// <summary>The subscription the shopper now holds.</summary>
    public SubscriptionDto? Subscription { get; set; }
}
