using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class SubscriptionPlanNotFoundException : Exception
{
    public SubscriptionPlanNotFoundException(string handle)
        : base($"Subscription plan '{handle}' was not found.") { }
}

public sealed class SubscriptionIdentityException : Exception
{
    public SubscriptionIdentityException() : base("The authenticated user could not be resolved.") { }
}
