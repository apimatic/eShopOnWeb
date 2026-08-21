using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

public sealed class SubscriptionOperationInProgressException : Exception
{
    public SubscriptionOperationInProgressException()
        : base("A subscription request for this plan is already in progress.") { }

    public SubscriptionOperationInProgressException(Exception innerException)
        : base("A subscription request for this plan is already in progress.", innerException) { }
}

public sealed class BillingProviderException : Exception
{
    public BillingProviderException(string message) : base(message) { }

    public BillingProviderException(string message, Exception innerException)
        : base(message, innerException) { }
}
