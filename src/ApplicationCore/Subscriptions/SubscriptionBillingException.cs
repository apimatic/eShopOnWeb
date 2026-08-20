using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

public class SubscriptionBillingException : Exception
{
    public SubscriptionBillingException(string message) : base(message)
    {
    }

    public SubscriptionBillingException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

public sealed class SubscriptionValidationException : SubscriptionBillingException
{
    public SubscriptionValidationException(string message) : base(message)
    {
    }
}
