using System;
using System.Net;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// The single failure type the SMS gateway surfaces. The Infrastructure implementation converts every
/// provider/SDK failure (API error, transport failure, unreadable body) into this type so callers have
/// one thing to handle. Carries the provider's HTTP status where one was returned.
/// </summary>
public sealed class SmsGatewayException : Exception
{
    public SmsGatewayException(string message, HttpStatusCode? statusCode = null, Exception? inner = null)
        : base(message, inner)
    {
        StatusCode = statusCode;
    }

    public HttpStatusCode? StatusCode { get; }
}
