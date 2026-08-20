using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public sealed class SubscriptionPlanNotFoundException : Exception
{
    public SubscriptionPlanNotFoundException(string productHandle)
        : base($"Subscription plan '{productHandle}' is not available.")
    {
    }
}
