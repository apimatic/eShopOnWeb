using System;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Thrown when a shopper asks to subscribe to a plan handle that is not part of
/// the configured Maxio product family catalog.
/// </summary>
public class PlanNotFoundException : Exception
{
    public PlanNotFoundException(string planHandle)
        : base($"No subscription plan with handle '{planHandle}' is available.")
    {
        PlanHandle = planHandle;
    }

    public string PlanHandle { get; }
}
