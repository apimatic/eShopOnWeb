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

    public CustomerSubscriptionDto Subscription { get; set; } = new CustomerSubscriptionDto();
}
