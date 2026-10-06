using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Response containing the list of subscription plans.
/// </summary>
public class ListSubscriptionPlansResponse : BaseResponse
{
    public ListSubscriptionPlansResponse(Guid correlationId) : base(correlationId)
    {
    }

    public ListSubscriptionPlansResponse()
    {
    }

    /// <summary>
    /// The subscription plans currently offered (from Maxio Advanced Billing).
    /// </summary>
    public List<SubscriptionPlanDto> SubscriptionPlans { get; set; } = new();
}
