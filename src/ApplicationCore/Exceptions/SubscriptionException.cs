using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// A subscription-billing failure already translated to a caller-safe message and HTTP status.
/// </summary>
public class SubscriptionException : Exception
{
    public int StatusCode { get; }

    public SubscriptionException(int statusCode, string message, Exception? inner = null) : base(message, inner)
    {
        StatusCode = statusCode;
    }
}
