using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionResponse : BaseResponse
{
    public const string StatusCreated = "created";
    public const string StatusAlreadySubscribed = "already_subscribed";
    public const string StatusPending = "pending";

    public CreateSubscriptionResponse(Guid correlationId) : base(correlationId)
    {
    }

    public CreateSubscriptionResponse()
    {
    }

    /// <summary><c>created</c>, <c>already_subscribed</c>, or <c>pending</c> (the provider has not confirmed yet).</summary>
    public string Status { get; set; } = string.Empty;

    public string? Message { get; set; }

    public SubscriptionDto? Subscription { get; set; }
}
