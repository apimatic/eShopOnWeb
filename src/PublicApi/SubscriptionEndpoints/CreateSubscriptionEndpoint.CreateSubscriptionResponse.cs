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

    /// <summary>"created" (201), "existing" (200) or "unknown" (202: not yet confirmed with Maxio).</summary>
    public string Outcome { get; set; } = string.Empty;

    public SubscriptionDto Subscription { get; set; } = new();
}
