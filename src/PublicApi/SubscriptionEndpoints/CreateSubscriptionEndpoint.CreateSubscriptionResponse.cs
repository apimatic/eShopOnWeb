using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionResponse : BaseResponse
{
    public CreateSubscriptionResponse(Guid correlationId) : base(correlationId)
    {
    }

    public CreateSubscriptionResponse()
    {
    }

    /// <summary><c>Created</c>, <c>AlreadySubscribed</c> or <c>Pending</c>.</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>The subscription's reference; stable for this user and plan.</summary>
    public string Reference { get; set; } = string.Empty;

    /// <summary>The subscription; null while <see cref="Status"/> is <c>Pending</c>.</summary>
    public SubscriptionDto? Subscription { get; set; }
}
