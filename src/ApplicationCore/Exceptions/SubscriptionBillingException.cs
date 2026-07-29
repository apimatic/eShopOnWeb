using System;
using System.Net;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Single failure type raised by the subscription-billing integration. It carries a caller-safe
/// <see cref="Exception.Message"/> (never a provider/SDK internal detail) and the HTTP
/// <see cref="StatusCode"/> the boundary should surface: a provider 4xx the caller can act on maps
/// to that same client 4xx; an unreachable provider, an unreadable success body, or an unknown
/// failure maps to 5xx.
/// </summary>
public class SubscriptionBillingException : Exception
{
    public SubscriptionBillingException(string message, HttpStatusCode statusCode = HttpStatusCode.BadGateway, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }

    /// <summary>HTTP status the API boundary should return for this failure.</summary>
    public HttpStatusCode StatusCode { get; }
}
