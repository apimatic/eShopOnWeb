using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when a caller requests a subscription to a plan handle that does not exist in the
/// configured product family. Surfaced to API callers as a not-found response.
/// </summary>
public class PlanNotFoundException : Exception
{
    public PlanNotFoundException(string planHandle)
        : base($"No subscription plan with handle '{planHandle}' exists in the configured product family.")
    {
        PlanHandle = planHandle;
    }

    public string PlanHandle { get; }
}
