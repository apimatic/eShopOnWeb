using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class SubscriptionPlanNotFoundException : Exception
{
    public SubscriptionPlanNotFoundException(string productHandle)
        : base($"A subscription plan with handle '{productHandle}' was not found.")
    {
    }
}
