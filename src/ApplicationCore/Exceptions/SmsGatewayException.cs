using System;
using System.Net;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The single failure type the SMS gateway surfaces, translated at the Twilio boundary from
/// provider errors, malformed responses, and transport failures alike. Carries an optional HTTP
/// status so callers can distinguish a caller-fixable rejection from a provider-side problem.
/// Never carries a phone number or message body.
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
