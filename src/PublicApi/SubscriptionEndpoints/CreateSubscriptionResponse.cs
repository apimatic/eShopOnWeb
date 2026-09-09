using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Response containing the subscription created (or already existing) for the user.
/// </summary>
public class CreateSubscriptionResponse : BaseResponse
{
    public CreateSubscriptionResponse() { }

    public CreateSubscriptionResponse(Guid correlationId) : base(correlationId) { }

    public SubscriptionDto? Subscription { get; set; }
}
