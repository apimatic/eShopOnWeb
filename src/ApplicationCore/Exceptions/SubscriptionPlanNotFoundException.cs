using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The requested plan does not exist in the configured billing catalog.
/// </summary>
public class SubscriptionPlanNotFoundException : Exception
{
    public SubscriptionPlanNotFoundException(string planHandle)
        : base($"No subscription plan with handle '{planHandle}' is available.")
    {
        PlanHandle = planHandle;
    }

    public string PlanHandle { get; }
}
