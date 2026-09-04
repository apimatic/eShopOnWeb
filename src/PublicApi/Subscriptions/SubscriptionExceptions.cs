using System;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class SubscriptionPlanNotFoundException : Exception
{
    public SubscriptionPlanNotFoundException(string planHandle)
        : base($"The subscription plan '{planHandle}' is not available.")
    {
    }
}

public sealed class SubscriptionUserNotFoundException : Exception
{
    public SubscriptionUserNotFoundException(string userName)
        : base($"The authenticated eShopOnWeb user '{userName}' could not be found.")
    {
    }
}
