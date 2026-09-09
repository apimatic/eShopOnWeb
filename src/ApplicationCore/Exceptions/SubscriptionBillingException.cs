using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when a subscription-billing operation cannot be completed. Carries the HTTP
/// status code that the API should surface to the caller (defaults to 502 Bad Gateway,
/// since most failures originate from the upstream Maxio API).
/// </summary>
public class SubscriptionBillingException : Exception
{
    public SubscriptionBillingException(string message, int statusCode = 502, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }

    /// <summary>The HTTP status code to return to the API caller.</summary>
    public int StatusCode { get; }
}
