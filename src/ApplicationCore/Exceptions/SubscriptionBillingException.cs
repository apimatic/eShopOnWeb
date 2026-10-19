using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// A subscription-billing failure with a caller-safe message and the HTTP status that describes it.
/// Never carries provider exception text or credentials.
/// </summary>
public class SubscriptionBillingException : Exception
{
    public SubscriptionBillingException(int statusCode, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }

    public int StatusCode { get; }
}
