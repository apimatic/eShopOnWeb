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

    /// <summary>Created, AlreadySubscribed, SubscribedToDifferentPlan, InProgress, OutcomeUnknown or UnknownPlan.</summary>
    public string Outcome { get; set; } = string.Empty;

    public SubscriptionDto? Subscription { get; set; }

    public string? Message { get; set; }
}
