using System;
using System.Collections.Generic;
namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Response confirming a subscription.
/// </summary>
public class CreateSubscriptionResponse : BaseResponse
{
    public CreateSubscriptionResponse(Guid correlationId) : base(correlationId) { }

    public SubscriptionDto? Subscription { get; set; }
}
