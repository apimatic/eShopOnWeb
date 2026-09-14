using System;
using System.Net;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when the billing provider (Maxio) rejects a request, is unreachable, or returns a
/// response that cannot be processed. Carries the upstream HTTP status when one is known so the
/// API boundary can map a provider 4xx to a client 4xx and everything else to a 5xx, rather than
/// collapsing every failure into one status. The message is caller-safe (no provider/SDK internals).
/// </summary>
public class SubscriptionBillingException : Exception
{
    /// <summary>
    /// The upstream HTTP status that caused this failure, when known. Null for transport failures
    /// or unreadable responses where no meaningful status exists.
    /// </summary>
    public HttpStatusCode? UpstreamStatusCode { get; }

    public SubscriptionBillingException(string message, HttpStatusCode? upstreamStatusCode = null, Exception? innerException = null)
        : base(message, innerException)
    {
        UpstreamStatusCode = upstreamStatusCode;
    }
}
