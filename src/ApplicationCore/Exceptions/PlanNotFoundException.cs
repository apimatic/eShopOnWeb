using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when a caller asks to subscribe to a plan handle that is not one of the plans available in
/// the configured product family. This is a caller error (surfaced as HTTP 404), not a provider fault.
/// </summary>
public class PlanNotFoundException : Exception
{
    public PlanNotFoundException(string planHandle)
        : base($"No subscription plan found with handle '{planHandle}'.")
    {
        PlanHandle = planHandle;
    }

    public string PlanHandle { get; }
}
