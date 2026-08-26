using System;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Thrown when a requested plan (product handle) is not offered in the configured product family.
/// </summary>
public class MaxioPlanNotFoundException : Exception
{
    public MaxioPlanNotFoundException(string productHandle)
        : base($"No subscription plan with handle '{productHandle}' is available.")
    {
        ProductHandle = productHandle;
    }

    public string ProductHandle { get; }
}
