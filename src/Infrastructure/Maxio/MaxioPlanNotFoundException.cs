using System;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>Thrown when a subscribe is requested for a plan (Maxio product) that does not exist in the configured product family.</summary>
public class MaxioPlanNotFoundException : Exception
{
    public string ProductHandle { get; }

    public MaxioPlanNotFoundException(string productHandle)
        : base($"Subscription plan '{productHandle}' was not found among the active plans in the configured Maxio product family.")
    {
        ProductHandle = productHandle;
    }
}