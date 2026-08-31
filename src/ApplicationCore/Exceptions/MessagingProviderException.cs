using System;
using System.Net;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// A failure talking to the messaging provider. Carries the provider's HTTP status when one
/// exists so the API boundary can map it deliberately. The message must always be
/// caller-safe: no credentials, no phone numbers, no raw provider payloads.
/// </summary>
public class MessagingProviderException : Exception
{
    public MessagingProviderException(string message, HttpStatusCode? statusCode, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }

    public HttpStatusCode? StatusCode { get; }
}
