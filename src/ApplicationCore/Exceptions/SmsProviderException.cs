using System;
using System.Net;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// A failure talking to the SMS provider: an API error (carrying the provider's HTTP status),
/// a transport failure, or an unreadable provider response (no status).
/// </summary>
public class SmsProviderException : Exception
{
    public SmsProviderException(string message, HttpStatusCode? statusCode, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }

    public HttpStatusCode? StatusCode { get; }
}
