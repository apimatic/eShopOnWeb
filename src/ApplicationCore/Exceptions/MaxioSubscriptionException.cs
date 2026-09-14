using System;
using System.Net;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when the Maxio subscription-billing integration cannot complete a request.
/// <see cref="StatusCode"/> carries the status the caller of eShopOnWeb's own API should see -
/// a provider 4xx maps back to the same client 4xx, and anything unreadable/unreachable maps to a 5xx.
/// </summary>
public class MaxioSubscriptionException : Exception
{
    public HttpStatusCode StatusCode { get; }

    public MaxioSubscriptionException(HttpStatusCode statusCode, string message) : base(message)
    {
        StatusCode = statusCode;
    }

    public MaxioSubscriptionException(HttpStatusCode statusCode, string message, Exception innerException)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }
}
