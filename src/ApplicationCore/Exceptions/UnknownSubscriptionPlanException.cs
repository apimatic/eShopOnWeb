using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when a subscribe request names a plan handle that is not one of the plans offered by the
/// configured product family. Surfaced to the caller as a 400.
/// </summary>
public class UnknownSubscriptionPlanException : Exception
{
    public UnknownSubscriptionPlanException(string planHandle)
        : base($"'{planHandle}' is not an available subscription plan.")
    {
        PlanHandle = planHandle;
    }

    public string PlanHandle { get; }
}
