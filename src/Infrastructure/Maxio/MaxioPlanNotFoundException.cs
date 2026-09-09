using System;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Raised when a subscription is requested for a plan that does not exist (or is not exposed)
/// in the configured Maxio product family.
/// </summary>
public class MaxioPlanNotFoundException : Exception
{
    public string PlanHandle { get; }

    public MaxioPlanNotFoundException(string planHandle)
        : base($"Subscription plan '{planHandle}' was not found among the available plans.")
    {
        PlanHandle = planHandle;
    }
}
