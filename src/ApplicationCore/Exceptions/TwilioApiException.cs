using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public class TwilioApiException : Exception
{
    public TwilioApiException(int statusCode, int? twilioErrorCode)
        : base(BuildMessage(statusCode, twilioErrorCode))
    {
        StatusCode = statusCode;
        TwilioErrorCode = twilioErrorCode;
    }

    public int StatusCode { get; }
    public int? TwilioErrorCode { get; }

    private static string BuildMessage(int statusCode, int? twilioErrorCode)
    {
        return twilioErrorCode is null
            ? $"Twilio request failed with HTTP {statusCode}."
            : $"Twilio request failed with HTTP {statusCode} (code {twilioErrorCode}).";
    }
}
