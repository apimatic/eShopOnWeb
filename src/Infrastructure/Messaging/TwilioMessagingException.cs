using System;
using System.Net;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

/// <summary>
/// The single failure type the Twilio gateway raises. Carries the provider's HTTP status where one was
/// returned, so the API boundary can map a caller-fixable 4xx apart from a provider-side 5xx/transport failure.
/// Never carries a secret or a phone number.
/// </summary>
public class TwilioMessagingException : Exception
{
    public HttpStatusCode? StatusCode { get; }

    public TwilioMessagingException(string message, HttpStatusCode? statusCode = null, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }
}
