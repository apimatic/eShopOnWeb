using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class CreateSubscriptionResponse
{
    public CreateSubscriptionResponse(Guid correlationId)
    {
        CorrelationId = correlationId;
    }

    public Guid CorrelationId { get; set; }
    public SubscriptionDto Subscription { get; set; } = new SubscriptionDto();
}
