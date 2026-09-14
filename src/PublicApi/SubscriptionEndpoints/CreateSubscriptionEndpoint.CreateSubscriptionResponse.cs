using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionResponse : BaseResponse
{
    public CreateSubscriptionResponse(Guid correlationId) : base(correlationId)
    {
    }

    public SubscriptionDto Subscription { get; set; } = new();

    /// <summary>False when an existing, still-active enrollment in this plan was returned instead of creating a new one.</summary>
    public bool Created { get; set; }
}
