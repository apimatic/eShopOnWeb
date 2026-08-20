using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class UnknownSubscriptionPlanException : Exception
{
    public UnknownSubscriptionPlanException(string productHandle)
        : base($"Unknown subscription plan '{productHandle}'.")
    {
        ProductHandle = productHandle;
    }

    public string ProductHandle { get; }
}
