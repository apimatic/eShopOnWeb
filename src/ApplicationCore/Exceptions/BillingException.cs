using System;
using System.Net;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The single failure type raised by the subscription-billing integration boundary.
/// The message is always caller-safe (no provider/SDK internals) and <see cref="StatusCode"/>
/// carries the HTTP status the API layer should surface:
/// a provider 4xx the caller can act on stays a 4xx; a transport failure, an unknown
/// error, or an unreadable success body becomes a 5xx.
/// </summary>
public class BillingException : Exception
{
    public BillingException(string message, HttpStatusCode statusCode, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }

    public HttpStatusCode StatusCode { get; }
}
