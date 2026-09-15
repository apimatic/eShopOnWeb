using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class SubscriptionPlanNotFoundException : Exception
{
    public SubscriptionPlanNotFoundException(string productHandle)
        : base($"No subscription plan with handle '{productHandle}' was found in the configured Maxio product family.")
    {
        ProductHandle = productHandle;
    }

    public string ProductHandle { get; }
}
