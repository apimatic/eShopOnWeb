using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class SubscriptionCreateResponse : BaseResponse
{
    public SubscriptionCreateResponse(Guid correlationId) : base(correlationId)
    {
    }

    public SubscriptionCreateResponse()
    {
    }

    public SubscriptionDetailsDto Subscription { get; set; } = new SubscriptionDetailsDto();

    public bool Created { get; set; }

    public bool AlreadySubscribed => !Created;
}
