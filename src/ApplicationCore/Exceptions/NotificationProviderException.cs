using System;
using System.Net;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// A failure at the messaging provider boundary. Carries the provider's HTTP status when there is one.
/// The message is always caller-safe (no secrets, no phone numbers, no SDK internals).
/// </summary>
public class NotificationProviderException : Exception
{
    public NotificationProviderException(string message, HttpStatusCode? statusCode = null, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }

    public HttpStatusCode? StatusCode { get; }
}
