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

    /// <summary><c>created</c>, <c>already_exists</c> or <c>pending</c>.</summary>
    public string Outcome { get; set; } = string.Empty;

    /// <summary>The subscription's reference at the billing provider (stable per user and plan).</summary>
    public string Reference { get; set; } = string.Empty;

    public string? Message { get; set; }

    /// <summary>The subscription; null while the outcome is pending.</summary>
    public SubscriptionDto? Subscription { get; set; }
}
