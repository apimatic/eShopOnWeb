using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when the requested subscription plan handle does not exist in the configured product family.
/// </summary>
public class SubscriptionPlanNotFoundException : Exception
{
    public SubscriptionPlanNotFoundException(string planHandle)
        : base($"The subscription plan '{planHandle}' is not available.")
    {
        PlanHandle = planHandle;
    }

    public string PlanHandle { get; }
}
