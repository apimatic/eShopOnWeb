using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when a configured plan handle does not resolve against the billing provider — i.e. the
/// sandbox was reseeded and configuration is stale. See UC0 in plan.md.
/// </summary>
public class PlanNotFoundException : Exception
{
    public PlanNotFoundException(string planHandle)
        : base($"No plan with handle '{planHandle}' was found in the configured product family. " +
               "Check the Maxio configuration against the seeded product family.")
    {
        PlanHandle = planHandle;
    }

    public string PlanHandle { get; }
}
