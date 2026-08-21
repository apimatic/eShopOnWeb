using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class UnknownSubscriptionPlanException : Exception
{
    public UnknownSubscriptionPlanException(string productHandle)
        : base($"Subscription plan '{productHandle}' was not found in the configured Maxio product family.")
    {
        ProductHandle = productHandle;
    }

    public string ProductHandle { get; }
}
