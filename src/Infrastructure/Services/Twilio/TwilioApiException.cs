using System;

namespace Microsoft.eShopWeb.Infrastructure.Services.Twilio;

public class TwilioApiException : Exception
{
    public TwilioApiException(int httpStatus, int? errorCode)
        : base($"Twilio request failed with HTTP {httpStatus} (code {errorCode}).")
    {
        HttpStatus = httpStatus;
        ErrorCode = errorCode;
    }

    public int HttpStatus { get; }
    public int? ErrorCode { get; }
}
