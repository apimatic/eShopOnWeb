using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public sealed class SubscriptionOutcomeUnknownException : Exception
{
    public SubscriptionOutcomeUnknownException()
        : base("The subscription request is being reconciled. Check your subscriptions before trying again.")
    {
    }
}
