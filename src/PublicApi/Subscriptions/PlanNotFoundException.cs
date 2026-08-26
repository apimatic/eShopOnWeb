using System;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

/// <summary>
/// Thrown when a requested subscription plan handle is not part of the
/// configured Maxio product family (or is archived).
/// </summary>
public class PlanNotFoundException : Exception
{
    public PlanNotFoundException(string productHandle)
        : base($"No subscribable plan with handle '{productHandle}' was found.")
    {
        ProductHandle = productHandle;
    }

    public string ProductHandle { get; }
}
