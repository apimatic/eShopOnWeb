using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class UnknownSubscriptionPlanException : Exception
{
    public UnknownSubscriptionPlanException(string productHandle)
        : base($"No subscription plan with handle '{productHandle}' exists in the configured Maxio product family.")
    {
        ProductHandle = productHandle;
    }

    public string ProductHandle { get; }
}
