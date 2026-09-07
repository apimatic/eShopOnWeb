using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Response containing the created subscription
/// </summary>
public class CreateSubscriptionResponse : BaseResponse
{
    public CreateSubscriptionResponse(Guid correlationId) : base(correlationId)
    {
    }

    public bool Success { get; set; }
    public SubscriptionDto? Subscription { get; set; }
}
