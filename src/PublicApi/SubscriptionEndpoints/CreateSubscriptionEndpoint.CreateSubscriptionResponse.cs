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

    /// <summary>This application's reference for the subscription request.</summary>
    public string Reference { get; set; } = string.Empty;

    /// <summary>Null while the outcome is <c>PendingConfirmation</c>.</summary>
    public SubscriptionDto? Subscription { get; set; }
}
