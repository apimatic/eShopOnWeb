using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when a shopper tries to subscribe to a plan handle that is not offered.
/// </summary>
public class SubscriptionPlanNotFoundException : Exception
{
    public SubscriptionPlanNotFoundException(string planHandle)
        : base($"No subscription plan was found with handle '{planHandle}'.")
    {
        PlanHandle = planHandle;
    }

    public string PlanHandle { get; }
}
