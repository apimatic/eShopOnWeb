using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when a requested subscription plan does not exist in the configured Maxio product family.
/// </summary>
public class PlanNotFoundException : Exception
{
    public PlanNotFoundException(string productHandle)
        : base($"Subscription plan '{productHandle}' was not found in the configured product family.")
    {
        ProductHandle = productHandle;
    }

    public string ProductHandle { get; }
}
