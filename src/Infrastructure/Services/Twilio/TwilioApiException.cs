using System;

namespace Microsoft.eShopWeb.Infrastructure.Services.Twilio;

public class TwilioApiException : Exception
{
    public TwilioApiException(int httpStatus, int? twilioCode, string? message)
        : base($"Twilio request failed with HTTP {httpStatus}" + (twilioCode.HasValue ? $" (code {twilioCode})." : "."))
    {
        HttpStatus = httpStatus;
        TwilioCode = twilioCode;
        ProviderMessage = message;
    }

    public int HttpStatus { get; }
    public int? TwilioCode { get; }
    public string? ProviderMessage { get; }
}
