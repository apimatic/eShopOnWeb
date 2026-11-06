using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListSubscriptionPlansResponse : BaseResponse
{
    public ListSubscriptionPlansResponse(Guid correlationId) : base(correlationId)
    {
    }

    public ListSubscriptionPlansResponse()
    {
    }

    public IReadOnlyList<PlanInfo> Plans { get; set; } = new List<PlanInfo>();

    /// <summary>True when the plan list was cut short by a page cap.</summary>
    public bool Truncated { get; set; }
}
