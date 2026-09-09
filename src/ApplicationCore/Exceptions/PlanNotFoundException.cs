using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when a subscription is requested for a plan (product handle) that does not
/// exist in the configured billing catalog.
/// </summary>
public class PlanNotFoundException : Exception
{
    public PlanNotFoundException(string productHandle)
        : base($"Subscription plan '{productHandle}' was not found.")
    {
        ProductHandle = productHandle;
    }

    public string ProductHandle { get; }
}
