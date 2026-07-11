using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class BillingProviderException : Exception
{
    public BillingProviderException(string message) : base(message)
    {
    }

    public BillingProviderException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

public class InvalidBillingOperationException : Exception
{
    public InvalidBillingOperationException(string message) : base(message)
    {
    }
}

public class SubscriptionNotFoundException : Exception
{
    public SubscriptionNotFoundException(string message) : base(message)
    {
    }
}
