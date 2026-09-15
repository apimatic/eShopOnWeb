using System;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class SubscriptionPlanNotFoundException : Exception
{
    public SubscriptionPlanNotFoundException(string planHandle)
        : base($"Subscription plan '{planHandle}' was not found in the configured Maxio product family.")
    {
    }
}

public sealed class SubscriptionOperationInProgressException : Exception
{
    public SubscriptionOperationInProgressException()
        : base("This subscription request is already being processed. Please retry shortly.")
    {
    }
}
