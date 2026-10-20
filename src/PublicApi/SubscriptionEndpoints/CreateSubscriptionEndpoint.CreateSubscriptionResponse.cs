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

    /// <summary><c>created</c>, <c>already_subscribed</c> or <c>pending</c>.</summary>
    public string Status { get; set; } = string.Empty;

    public string PlanHandle { get; set; } = string.Empty;

    public string? Message { get; set; }

    /// <summary>Null only while <see cref="Status"/> is <c>pending</c>.</summary>
    public SubscriptionDto? Subscription { get; set; }
}
