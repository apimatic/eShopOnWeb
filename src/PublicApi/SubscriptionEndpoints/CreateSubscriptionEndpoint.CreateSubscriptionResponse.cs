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

    /// <summary><c>Created</c>, <c>AlreadySubscribed</c> or <c>PendingConfirmation</c>.</summary>
    public string Outcome { get; set; } = string.Empty;

    public string PlanHandle { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    /// <summary>The subscription as Maxio reports it; null while the outcome is pending confirmation.</summary>
    public SubscriptionDto? Subscription { get; set; }
}
