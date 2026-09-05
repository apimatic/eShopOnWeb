using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when a requested subscription plan handle does not exist within the configured
/// Maxio product family.
/// </summary>
public class PlanNotFoundException : Exception
{
    public PlanNotFoundException(string planHandle) : base($"No subscription plan found with handle '{planHandle}'")
    {
    }
}
