using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Models.Subscription;
using Microsoft.eShopWeb.PublicApi;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Response listing the available subscription plans
/// </summary>
public class SubscriptionPlanListResponse : BaseResponse
{
    public List<SubscriptionPlanDto> Plans { get; } = new();
}