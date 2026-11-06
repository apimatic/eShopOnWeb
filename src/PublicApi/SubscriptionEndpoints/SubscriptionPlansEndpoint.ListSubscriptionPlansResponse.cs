using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class ListSubscriptionPlansResponse : BaseResponse
{
    public ListSubscriptionPlansResponse(Guid correlationId) : base(correlationId)
    {
    }

    public ListSubscriptionPlansResponse()
    {
    }

    public List<SubscriptionPlan> Plans { get; set; } = new();

    /// <summary>True when the provider list was cut short by a safety cap.</summary>
    public bool IsTruncated { get; set; }
}
