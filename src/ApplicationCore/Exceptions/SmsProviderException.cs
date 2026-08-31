using System;
using System.Net;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// A failure at the messaging-provider boundary. Carries the provider's HTTP status
/// when one was received; null for transport failures and unreadable responses.
/// </summary>
public class SmsProviderException : Exception
{
    public HttpStatusCode? StatusCode { get; }

    public SmsProviderException(string message, HttpStatusCode? statusCode, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }
}
