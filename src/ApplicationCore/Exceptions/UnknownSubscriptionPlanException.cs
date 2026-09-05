using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when a caller requests a subscription plan handle that does not exist in the configured
/// Maxio product family.
/// </summary>
public class UnknownSubscriptionPlanException : Exception
{
    public UnknownSubscriptionPlanException(string planHandle) : base($"Unknown subscription plan '{planHandle}'.")
    {
    }
}
