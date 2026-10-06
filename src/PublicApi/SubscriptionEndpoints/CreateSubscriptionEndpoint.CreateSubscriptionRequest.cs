using System;
using Microsoft.eShopWeb.PublicApi;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Request to subscribe the authenticated user to a plan.
/// </summary>
public class CreateSubscriptionRequest : BaseRequest
{
    /// <summary>
    /// The stable handle of the plan (Maxio product handle) to subscribe to,
    /// e.g. "eshop-pro". Handles are stable across Maxio re-seeds; numeric ids are not.
    /// </summary>
    public string PlanHandle { get; set; }
}