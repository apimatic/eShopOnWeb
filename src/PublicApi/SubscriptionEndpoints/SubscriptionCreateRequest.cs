using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Request to subscribe the authenticated user to a plan.
/// </summary>
public class SubscriptionCreateRequest : BaseRequest
{
    /// <summary>Handle of the plan to subscribe to (e.g. "eshop-pro"). Required.</summary>
    public string PlanHandle { get; set; } = string.Empty;
}
