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

    /// <summary><c>created</c> (201), <c>existing</c> (200, nothing new created) or <c>pending</c> (202, outcome being confirmed).</summary>
    public string Status { get; set; } = string.Empty;

    public string PlanHandle { get; set; } = string.Empty;

    /// <summary>The reference this subscribe attempt was sent to Maxio with (lookup key while pending).</summary>
    public string? SubscriptionReference { get; set; }

    public SubscriptionDto? Subscription { get; set; }
}
