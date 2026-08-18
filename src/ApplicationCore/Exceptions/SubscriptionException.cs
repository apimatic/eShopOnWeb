using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class SubscriptionException : Exception
{
    public SubscriptionException(string message, int statusCode = 400) : base(message)
    {
        StatusCode = statusCode;
    }

    public int StatusCode { get; }
}
