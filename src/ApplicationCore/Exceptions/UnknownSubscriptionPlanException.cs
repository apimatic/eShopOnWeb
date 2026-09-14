using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when an operation references a subscription plan that does not exist
/// or is not available for enrollment.
/// </summary>
public class UnknownSubscriptionPlanException : Exception
{
    public UnknownSubscriptionPlanException(string planHandle)
        : base($"Unknown subscription plan '{planHandle}'.")
    {
        PlanHandle = planHandle;
    }

    public string PlanHandle { get; }
}
