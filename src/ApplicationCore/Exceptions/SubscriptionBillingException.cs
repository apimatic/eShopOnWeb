using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised at the billing boundary when a subscription operation cannot be completed. Carries a
/// caller-safe message and the HTTP status the caller should receive — never a raw provider/SDK
/// message or type name.
/// </summary>
public class SubscriptionBillingException : Exception
{
    /// <summary>The HTTP status the API boundary should return for this failure (e.g. 400, 404, 502).</summary>
    public int StatusCode { get; }

    public SubscriptionBillingException(string message, int statusCode = 502, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }
}
