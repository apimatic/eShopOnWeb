using System;
using System.Net;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The single failure type the SMS provider integration surfaces to the rest of the application.
/// Both provider API errors (a non-2xx response) and connection failures (unreachable host,
/// timeout, dropped socket) are converted to this type at the gateway boundary, so callers have
/// one exception to reason about rather than the provider SDK's own types.
///
/// <see cref="StatusCode"/> carries the provider's HTTP status when there was one (a genuine API
/// rejection); it is null for a transport failure, where nothing answered.
/// Messages on this exception must never contain a destination phone number.
/// </summary>
public class SmsGatewayException : Exception
{
    public SmsGatewayException(string message, HttpStatusCode? statusCode = null, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }

    public HttpStatusCode? StatusCode { get; }
}
