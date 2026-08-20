using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class SubscriptionBillingException : Exception
{
    public SubscriptionBillingException(string message, int statusCode = 502) : base(message)
    {
        StatusCode = statusCode;
    }

    public SubscriptionBillingException(string message, Exception innerException, int statusCode = 502)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }

    public int StatusCode { get; }
}
