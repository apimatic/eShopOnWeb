using System;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class SubscriptionPlanNotFoundException : Exception
{
    public SubscriptionPlanNotFoundException(string productHandle)
        : base($"Subscription plan '{productHandle}' was not found in the configured Maxio product family.")
    {
    }
}

public sealed class MaxioDataIntegrityException : Exception
{
    public MaxioDataIntegrityException(string message) : base(message)
    {
    }
}
