using System;
using System.Net;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

/// <summary>
/// The single failure type the messaging gateway raises, so callers handle one exception rather than the
/// several the SDK and transport can throw. Carries the provider HTTP status where one is available (null
/// for a transport failure, where nothing answered). Never carries the message body or destination number.
/// </summary>
public sealed class TwilioProviderException : Exception
{
    public TwilioProviderException(string message, HttpStatusCode? statusCode, Exception? inner)
        : base(message, inner)
    {
        StatusCode = statusCode;
    }

    public HttpStatusCode? StatusCode { get; }
}
