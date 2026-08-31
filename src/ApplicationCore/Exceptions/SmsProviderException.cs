using System;
using System.Net;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// A failure at the messaging-provider boundary. Carries the provider's HTTP status when
/// one exists so callers can distinguish a rejection of their request from an outage.
/// </summary>
public class SmsProviderException : Exception
{
    public SmsProviderException(string message, HttpStatusCode? statusCode = null, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }

    public HttpStatusCode? StatusCode { get; }
}
