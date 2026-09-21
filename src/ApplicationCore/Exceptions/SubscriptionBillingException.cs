using System;
using System.Net;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Boundary error type for the subscription-billing integration. Provider API errors, transport
/// failures, and unreadable responses are all translated into this single type so callers have one
/// failure to handle. <see cref="StatusCode"/> carries the provider HTTP status when one is known
/// (null for transport/unknown failures); <see cref="ProviderMessage"/> carries a caller-safe summary.
/// </summary>
public class SubscriptionBillingException : Exception
{
    public SubscriptionBillingException(string message, HttpStatusCode? statusCode = null, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        ProviderMessage = message;
    }

    /// <summary>Provider HTTP status, when the failure carried one; null for transport/unknown failures.</summary>
    public HttpStatusCode? StatusCode { get; }

    /// <summary>Caller-safe description of the failure (never a raw SDK/framework exception string).</summary>
    public string ProviderMessage { get; }
}
