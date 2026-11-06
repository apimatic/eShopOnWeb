using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.PublicApi.Maxio;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListSubscriptionPlansRequest : BaseRequest
{
}

public class ListSubscriptionPlansResponse : BaseResponse
{
    public ListSubscriptionPlansResponse(Guid correlationId) : base(correlationId)
    {
    }

    public ListSubscriptionPlansResponse()
    {
    }

    public IReadOnlyList<PlanDto> Plans { get; set; } = Array.Empty<PlanDto>();

    /// <summary>True when more plans exist than could be returned.</summary>
    public bool Truncated { get; set; }
}
