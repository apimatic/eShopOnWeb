using System;
using System.Net;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The single failure type the SMS gateway raises, whatever went wrong underneath (a provider error
/// response, a transport failure, a timeout, or an unreadable body). <see cref="StatusCode"/> carries the
/// provider's HTTP status when one was available, so a boundary can tell "the caller's request was bad"
/// from "the provider is unavailable". The message is always caller-safe (no provider internals, no
/// phone number, no secret).
/// </summary>
public class SmsGatewayException : Exception
{
    public SmsGatewayException(string message, HttpStatusCode? statusCode = null, Exception? inner = null)
        : base(message, inner)
    {
        StatusCode = statusCode;
    }

    public HttpStatusCode? StatusCode { get; }
}
