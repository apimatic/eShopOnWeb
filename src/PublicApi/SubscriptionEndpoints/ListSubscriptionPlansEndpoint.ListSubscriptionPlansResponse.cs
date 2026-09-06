using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListSubscriptionPlansResponse : BaseResponse
{
    public ListSubscriptionPlansResponse(Guid correlationId) : base(correlationId)
    {
    }

    public IReadOnlyList<SubscriptionPlanDto>? Plans { get; set; }
    public string? ErrorMessage { get; set; }
}
