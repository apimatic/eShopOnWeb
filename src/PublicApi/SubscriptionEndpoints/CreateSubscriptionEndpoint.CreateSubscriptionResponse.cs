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

    /// <summary>"created", "already_subscribed" or "pending_confirmation".</summary>
    public string Status { get; set; } = string.Empty;

    public string PlanHandle { get; set; } = string.Empty;

    /// <summary>The reference the subscription carries in the billing system.</summary>
    public string SubscriptionReference { get; set; } = string.Empty;

    /// <summary>Null only when <see cref="Status"/> is "pending_confirmation".</summary>
    public SubscriptionDto? Subscription { get; set; }
}
