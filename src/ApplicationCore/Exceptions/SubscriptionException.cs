using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// A subscription-billing failure that is safe to show a caller. <see cref="StatusCode"/> is the HTTP status to present.
/// </summary>
public class SubscriptionException : Exception
{
    public int StatusCode { get; }

    public SubscriptionException(int statusCode, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }
}
