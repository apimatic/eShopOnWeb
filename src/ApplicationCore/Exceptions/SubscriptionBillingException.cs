using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public sealed class SubscriptionBillingException : Exception
{
    public int StatusCode { get; }

    public SubscriptionBillingException(int statusCode, string message) : base(message)
    {
        StatusCode = statusCode;
    }
}
