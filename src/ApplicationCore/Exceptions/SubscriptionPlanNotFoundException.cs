using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when a requested subscription plan (Maxio product handle) is not found
/// in the configured product family.
/// </summary>
public class SubscriptionPlanNotFoundException : Exception
{
    public SubscriptionPlanNotFoundException(string productHandle)
        : base($"Subscription plan '{productHandle}' was not found in the configured product family.")
    {
        ProductHandle = productHandle;
    }

    public string ProductHandle { get; }
}