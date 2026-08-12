using System;
using System.Net;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

/// <summary>
/// Raised when the Twilio API returns a non-success HTTP status. The message is sanitised so it never
/// carries a phone number.
/// </summary>
public class TwilioApiException : Exception
{
    public TwilioApiException(HttpStatusCode statusCode, int? twilioCode, string message)
        : base(message)
    {
        StatusCode = statusCode;
        TwilioCode = twilioCode;
    }

    public HttpStatusCode StatusCode { get; }
    public int? TwilioCode { get; }
}
