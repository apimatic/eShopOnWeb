using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when a caller asks to subscribe to a plan handle that this site does not offer.
/// </summary>
public class SubscriptionPlanNotFoundException : Exception
{
    public SubscriptionPlanNotFoundException(string planHandle)
        : this(planHandle, $"No subscription plan with handle '{planHandle}' is available.")
    {
    }

    public SubscriptionPlanNotFoundException(string planHandle, string message) : base(message)
    {
        PlanHandle = planHandle;
    }

    public string PlanHandle { get; } = string.Empty;
}
