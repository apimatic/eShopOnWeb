using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when a subscription plan (Maxio product) cannot be found.
/// </summary>
public class PlanNotFoundException : Exception
{
    public PlanNotFoundException(string planHandle)
        : base($"Subscription plan '{planHandle}' was not found.")
    {
    }
}
