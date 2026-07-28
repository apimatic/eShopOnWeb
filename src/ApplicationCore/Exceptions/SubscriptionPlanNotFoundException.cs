using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when a requested plan handle is not one of the plans available in the configured
/// product family. Signals a client error (bad plan handle) rather than a server fault.
/// </summary>
public class SubscriptionPlanNotFoundException : Exception
{
    public SubscriptionPlanNotFoundException(string planHandle)
        : base($"No subscription plan with handle '{planHandle}' is available in the configured product family.")
    {
    }
}
