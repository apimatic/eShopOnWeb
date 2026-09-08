using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class SubscriptionAccessDeniedException : Exception
{
    public SubscriptionAccessDeniedException()
        : base("The caller could not be mapped to a known eShopOnWeb user.")
    {
    }
}
