using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListSubscriptionPlansResponse
{
    public ListSubscriptionPlansResponse(Guid correlationId)
    {
        CorrelationId = correlationId;
    }

    public Guid CorrelationId { get; set; }
    public IReadOnlyList<SubscriptionPlanDto> Plans { get; set; } = Array.Empty<SubscriptionPlanDto>();
}
