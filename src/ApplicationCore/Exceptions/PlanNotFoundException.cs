using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class PlanNotFoundException : Exception
{
    public PlanNotFoundException(string productHandle)
        : base($"Subscription plan '{productHandle}' was not found in the configured product family.")
    {
        ProductHandle = productHandle;
    }

    public string ProductHandle { get; }
}
