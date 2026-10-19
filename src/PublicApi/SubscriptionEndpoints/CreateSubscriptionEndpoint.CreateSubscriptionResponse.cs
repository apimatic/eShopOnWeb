using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionResponse : BaseResponse
{
    public const string StatusCreated = "created";
    public const string StatusAlreadySubscribed = "already_subscribed";
    public const string StatusPendingConfirmation = "pending_confirmation";

    public CreateSubscriptionResponse(Guid correlationId) : base(correlationId)
    {
    }

    public CreateSubscriptionResponse()
    {
    }

    /// <summary>created | already_subscribed | pending_confirmation</summary>
    public string Status { get; set; } = string.Empty;

    public string? Message { get; set; }

    /// <summary>The subscription; null while the outcome is pending confirmation.</summary>
    public SubscriptionDto? Subscription { get; set; }
}
