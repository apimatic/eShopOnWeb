using System;
using System.Net;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised at the SMS-gateway boundary when the provider (or the transport to it) fails in a way the
/// caller of an operator action needs to see. Carries the provider HTTP status where one is known so
/// the API boundary can map it (our-fault credentials/quota vs the caller's request) without leaking
/// SDK types or message content.
/// </summary>
public class SmsGatewayException : Exception
{
    public HttpStatusCode? StatusCode { get; }

    public SmsGatewayException(string message, HttpStatusCode? statusCode = null, Exception? inner = null)
        : base(message, inner)
    {
        StatusCode = statusCode;
    }
}
