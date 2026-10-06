using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when a subscription plan (Maxio product) cannot be found by its handle.
/// </summary>
public class PlanNotFoundException : Exception
{
    public string PlanHandle { get; }

    public PlanNotFoundException(string planHandle)
        : base($"A subscription plan with handle '{planHandle}' was not found.")
    {
        PlanHandle = planHandle;
    }
}