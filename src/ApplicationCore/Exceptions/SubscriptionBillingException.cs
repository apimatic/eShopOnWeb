using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Single failure type raised at the Maxio billing boundary. Carries a caller-safe
/// <see cref="Message"/> (never an SDK/JSON internal string) and a <see cref="StatusCode"/>
/// so the API can map provider 4xx → client 4xx and everything unknown → 5xx.
/// </summary>
public class SubscriptionBillingException : Exception
{
    /// <summary>The HTTP status the API should surface to its caller.</summary>
    public int StatusCode { get; }

    public SubscriptionBillingException(string message, int statusCode = 502, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }
}
