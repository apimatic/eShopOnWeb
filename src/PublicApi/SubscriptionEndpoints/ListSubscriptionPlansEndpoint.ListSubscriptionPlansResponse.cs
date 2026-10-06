using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Response for GET api/subscription-plans.
/// </summary>
public class ListSubscriptionPlansResponse : BaseResponse
{
    public ListSubscriptionPlansResponse(Guid correlationId) : base(correlationId)
    {
    }

    public ListSubscriptionPlansResponse()
    {
    }

    public System.Collections.Generic.List<SubscriptionPlanDto> SubscriptionPlans { get; set; } = new();
}