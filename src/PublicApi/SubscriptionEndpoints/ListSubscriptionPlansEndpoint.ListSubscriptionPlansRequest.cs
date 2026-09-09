using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Request for listing subscription plans.
/// </summary>
public class ListSubscriptionPlansRequest : BaseRequest
{
}

/// <summary>
/// Response containing the available subscription plans.
/// </summary>
public class ListSubscriptionPlansResponse : BaseResponse
{
    public ListSubscriptionPlansResponse(Guid correlationId) : base(correlationId)
    {
    }

    public List<SubscriptionPlanDto> SubscriptionPlans { get; set; } = new();
}
