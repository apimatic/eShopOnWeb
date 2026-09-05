using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when a caller asks to subscribe to a plan handle that isn't one of the currently available plans.
/// </summary>
public class SubscriptionPlanNotFoundException : Exception
{
    public SubscriptionPlanNotFoundException(string planHandle)
        : base($"'{planHandle}' is not a known subscription plan.")
    {
    }
}
