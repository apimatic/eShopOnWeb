using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when a subscription plan (product handle) cannot be resolved
/// in the billing system or is not part of the configured plan family.
/// </summary>
public class UnknownSubscriptionPlanException : Exception
{
    public string PlanHandle { get; }

    public UnknownSubscriptionPlanException(string planHandle) : base($"Subscription plan '{planHandle}' was not found.")
    {
        PlanHandle = planHandle;
    }
}