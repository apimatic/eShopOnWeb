using System;
using System.Net;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public sealed class SubscriptionBillingException : Exception
{
    public SubscriptionBillingException(HttpStatusCode statusCode, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }

    public HttpStatusCode StatusCode { get; }
}
