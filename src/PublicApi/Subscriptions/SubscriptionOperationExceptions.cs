using System;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class SubscriptionOperationInProgressException : Exception
{
    public SubscriptionOperationInProgressException()
        : base("A subscription request for this plan is already in progress. Retry shortly.")
    {
    }
}
