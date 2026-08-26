using System;
using Microsoft.eShopWeb.ApplicationCore.Models;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionResponse : BaseResponse
{
    public CreateSubscriptionResponse(Guid correlationId) : base(correlationId)
    {
    }

    public CreateSubscriptionResponse()
    {
    }

    public CustomerSubscriptionDto? Subscription { get; set; }

    /// <summary>
    /// False when an existing active subscription was returned instead of creating a new one.
    /// </summary>
    public bool Created { get; set; }
}
