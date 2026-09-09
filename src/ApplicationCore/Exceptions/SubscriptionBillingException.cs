using System;
using System.Net;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when the subscription-billing system of record fails or rejects a request.
/// Carries the HTTP status the caller should see; the message is always caller-safe
/// (it never leaks SDK or framework internals).
/// </summary>
public class SubscriptionBillingException : Exception
{
    public HttpStatusCode StatusCode { get; }

    public SubscriptionBillingException(HttpStatusCode statusCode, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }

    public static SubscriptionBillingException ProviderError(Exception innerException) =>
        new(HttpStatusCode.BadGateway,
            "The billing system could not be reached or returned an unreadable response. The outcome of the request is unknown; retry the operation.", innerException);
}
