using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when a request references a subscription plan that is not offered in the configured product family.
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
