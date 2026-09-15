using System;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

public class TwilioRequestException : Exception
{
    public TwilioRequestException(int httpStatus, int? twilioCode)
        : base(FormatMessage(httpStatus, twilioCode))
    {
        HttpStatus = httpStatus;
        TwilioCode = twilioCode;
    }

    public int HttpStatus { get; }
    public int? TwilioCode { get; }

    private static string FormatMessage(int httpStatus, int? twilioCode)
        => twilioCode is null
            ? $"Twilio request failed with HTTP {httpStatus}."
            : $"Twilio request failed with HTTP {httpStatus}, code {twilioCode}.";
}
