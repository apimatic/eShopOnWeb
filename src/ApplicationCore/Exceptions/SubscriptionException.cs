using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// A subscription operation failed. <see cref="StatusCode"/> is the HTTP status the caller should see;
/// the message is safe to return to the caller.
/// </summary>
public class SubscriptionException : Exception
{
    public int StatusCode { get; }

    public SubscriptionException(string message, int statusCode, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }
}
