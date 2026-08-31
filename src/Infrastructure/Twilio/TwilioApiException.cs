using System;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

/// <summary>
/// An error response from the Twilio API. The message may reference request details,
/// so it must never be written to logs alongside destination numbers.
/// </summary>
public class TwilioApiException : Exception
{
    public TwilioApiException(int? errorCode, string message, int httpStatusCode)
        : base(message)
    {
        ErrorCode = errorCode;
        HttpStatusCode = httpStatusCode;
    }

    public int? ErrorCode { get; }
    public int HttpStatusCode { get; }
}
