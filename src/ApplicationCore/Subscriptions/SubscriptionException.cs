using System;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// A subscription failure with a caller-safe message and the HTTP status it should surface as.
/// </summary>
public class SubscriptionException : Exception
{
    public int StatusCode { get; }

    public SubscriptionException(int statusCode, string message, Exception? inner = null) : base(message, inner)
    {
        StatusCode = statusCode;
    }
}
