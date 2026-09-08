using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionServices;

/// <summary>
/// Raised when a shopper asks to subscribe to a plan handle that is not among the plans
/// offered by the configured Maxio product family.
/// </summary>
public sealed class SubscriptionPlanNotFoundException : Exception
{
    public SubscriptionPlanNotFoundException(string productHandle)
        : base($"The plan '{productHandle}' is not available in the Maxio subscription catalog.")
    {
        ProductHandle = productHandle;
    }

    public string ProductHandle { get; }
}
