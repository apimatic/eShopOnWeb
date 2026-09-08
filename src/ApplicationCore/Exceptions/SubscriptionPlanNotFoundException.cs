using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when a shopper asks to subscribe to a plan handle that does not exist in the Maxio
/// product family configured for the storefront.
/// </summary>
public class SubscriptionPlanNotFoundException : Exception
{
    public SubscriptionPlanNotFoundException(string productHandle)
        : base($"No subscription plan with handle '{productHandle}' is available.")
    {
        ProductHandle = productHandle;
    }

    public string ProductHandle { get; }
}
