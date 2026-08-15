using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Single failure type raised by <see cref="Interfaces.ISubscriptionBillingService"/>.
/// Carries a caller-safe message and the HTTP status code the API boundary should surface:
/// a provider client-error (4xx) that the caller can act on is preserved as that 4xx, while a
/// transport failure, an unreadable success response, or an unknown error surfaces as a 5xx.
/// </summary>
public class SubscriptionBillingException : Exception
{
    /// <summary>HTTP status code to surface to the caller. Defaults to 502 (Bad Gateway).</summary>
    public int StatusCode { get; }

    public SubscriptionBillingException(string message, int statusCode = 502, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }
}
