using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Thrown when a subscribe request references a plan handle that does not exist
/// in the configured product family.
/// </summary>
public class UnknownSubscriptionPlanException : Exception
{
    public UnknownSubscriptionPlanException(string planHandle)
        : base($"No subscription plan with handle '{planHandle}' exists in the configured product family.")
    {
        PlanHandle = planHandle;
    }

    public string PlanHandle { get; }
}
