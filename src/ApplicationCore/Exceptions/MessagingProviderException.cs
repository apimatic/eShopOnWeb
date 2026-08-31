using System;
using System.Net;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The single failure type leaving the messaging-provider boundary. Carries the provider's
/// HTTP status when there is one; the message is always caller-safe (never contains provider
/// response bodies, destination numbers, or credentials).
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
