using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class SubscriptionBillingException : Exception
{
    public SubscriptionBillingException(string message, int statusCode, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }

    public int StatusCode { get; }
}
