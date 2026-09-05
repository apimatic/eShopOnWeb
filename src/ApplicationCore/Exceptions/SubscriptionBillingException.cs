using System;
using System.Net;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when the recurring-billing provider rejects a request or is unreachable. Carries the
/// provider's HTTP status (or a 5xx for a transport/parsing failure) so the API boundary can
/// surface a distinct status instead of collapsing every failure to 500.
/// </summary>
public class SubscriptionBillingException : Exception
{
    public HttpStatusCode StatusCode { get; }

    public SubscriptionBillingException(HttpStatusCode statusCode, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }
}
