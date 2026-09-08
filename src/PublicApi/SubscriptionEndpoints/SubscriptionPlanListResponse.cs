using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Response for listing subscription plans.
/// </summary>
public class SubscriptionPlanListResponse : BaseResponse
{
    public SubscriptionPlanListResponse(Guid correlationId) : base(correlationId)
    {
    }

    public SubscriptionPlanListResponse()
    {
    }

    /// <summary>Handle of the Maxio product family the plans belong to.</summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    public List<SubscriptionPlanDto> Plans { get; set; } = new();
}
