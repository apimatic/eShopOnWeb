using System;
using Microsoft.eShopWeb.PublicApi.SubscriptionServices;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionResponse : BaseResponse
{
    public CreateSubscriptionResponse(Guid correlationId) : base(correlationId)
    {
    }

    public CreateSubscriptionResponse()
    {
    }

    public SubscriptionDetailsDto Subscription { get; set; } = new SubscriptionDetailsDto();

    public bool CreatedNew { get; set; }
}
