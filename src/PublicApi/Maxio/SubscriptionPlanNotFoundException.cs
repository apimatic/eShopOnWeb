using System;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Thrown when a requested plan handle does not exist in the configured Maxio product family.
/// </summary>
public class SubscriptionPlanNotFoundException : Exception
{
    public SubscriptionPlanNotFoundException(string productHandle)
        : base($"No subscription plan with handle '{productHandle}' exists in the configured product family.")
    {
        ProductHandle = productHandle;
    }

    public string ProductHandle { get; }
}
