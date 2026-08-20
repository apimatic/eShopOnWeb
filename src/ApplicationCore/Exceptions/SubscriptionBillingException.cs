using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Caller-safe failure from the subscription billing provider boundary.
/// StatusCode is the HTTP status the PublicApi should return.
/// </summary>
public sealed class SubscriptionBillingException : Exception
{
    public SubscriptionBillingException(int statusCode, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }

    public int StatusCode { get; }
}
