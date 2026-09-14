using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when a shopper references a subscription plan that is not offered.
/// </summary>
public class UnknownSubscriptionPlanException : Exception
{
    public UnknownSubscriptionPlanException(string planHandle)
        : base($"Subscription plan '{planHandle}' is not offered.")
    {
        PlanHandle = planHandle;
    }

    public string PlanHandle { get; }
}
