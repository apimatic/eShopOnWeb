using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class SubscriptionPlanNotFoundException : Exception
{
    public SubscriptionPlanNotFoundException(string productHandle)
        : base($"No subscription plan with handle '{productHandle}' is available.")
    {
        ProductHandle = productHandle;
    }

    public string ProductHandle { get; }
}
