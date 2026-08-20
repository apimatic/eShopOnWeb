using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class TwilioMessagingException : Exception
{
    public TwilioMessagingException(int? errorCode, int statusCode)
        : base($"Twilio request failed with HTTP {statusCode} and error {errorCode?.ToString() ?? "unknown"}.")
    {
        ErrorCode = errorCode;
        StatusCode = statusCode;
    }

    public int? ErrorCode { get; }
    public int StatusCode { get; }
}
