using System;
using System.Net;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The single failure type the messaging integration surfaces to callers. Wraps any provider API error
/// or transport failure. <see cref="StatusCode"/> is the provider's HTTP status when one was received,
/// or null for a transport failure / timeout (nothing answered).
/// </summary>
public class MessagingProviderException : Exception
{
    public HttpStatusCode? StatusCode { get; }

    public MessagingProviderException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }

    public MessagingProviderException(string message, HttpStatusCode statusCode, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }
}
