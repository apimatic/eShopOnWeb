using System;
using System.Net;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The single failure type that crosses the messaging-provider boundary.
/// Carries the provider's HTTP status when there is one, so callers can map
/// client-caused rejections (4xx) back to the caller and treat the rest as provider faults.
/// </summary>
public class MessagingException : Exception
{
    public MessagingException(string message, HttpStatusCode? statusCode = null, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }

    /// <summary>The provider's HTTP status, or null for transport/timeout/unknown failures.</summary>
    public HttpStatusCode? StatusCode { get; }
}
