using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Raised when a requested plan handle is not offered by the configured product family.
/// </summary>
public class PlanNotFoundException : Exception
{
    public PlanNotFoundException(string handle)
        : base($"No subscription plan with handle '{handle}' is available in the configured product family.")
    {
        Handle = handle;
    }

    public string Handle { get; }
}
