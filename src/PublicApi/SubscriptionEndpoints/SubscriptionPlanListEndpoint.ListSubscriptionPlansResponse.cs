using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListSubscriptionPlansResponse : BaseResponse
{
    public ListSubscriptionPlansResponse(Guid correlationId) : base(correlationId)
    {
    }

    public ListSubscriptionPlansResponse()
    {
    }

    public List<SubscriptionPlanDto> Plans { get; set; } = new();

    /// <summary>
    /// False when the plan list was cut short (the billing system returned more plans than the page cap) —
    /// some plans may be missing from <see cref="Plans"/>.
    /// </summary>
    public bool IsComplete { get; set; }
}
