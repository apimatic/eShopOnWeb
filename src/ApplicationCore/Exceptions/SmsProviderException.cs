using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when the messaging provider rejects a request. The message is sanitized on purpose: it never
/// carries a phone number or message body, so it is safe to log. Programmatic callers branch on
/// <see cref="TwilioErrorCode"/> / <see cref="HttpStatusCode"/>.
/// </summary>
public class SmsProviderException : Exception
{
    public int HttpStatusCode { get; }
    public int? TwilioErrorCode { get; }

    public SmsProviderException(int httpStatusCode, int? twilioErrorCode, string sanitizedMessage)
        : base(sanitizedMessage)
    {
        HttpStatusCode = httpStatusCode;
        TwilioErrorCode = twilioErrorCode;
    }
}
