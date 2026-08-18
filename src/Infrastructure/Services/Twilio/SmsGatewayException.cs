using System;

namespace Microsoft.eShopWeb.Infrastructure.Services.Twilio;

/// <summary>
/// Raised when a Twilio messaging call fails. Its message deliberately carries only the HTTP
/// status and Twilio's numeric error code — never the free-text provider message, which can
/// contain the destination phone number — so it is safe to log.
/// </summary>
public class SmsGatewayException : Exception
{
    public SmsGatewayException(int httpStatus, int? providerErrorCode)
        : base($"Twilio messaging request failed (HTTP {httpStatus}"
              + (providerErrorCode is not null ? $", provider code {providerErrorCode}" : string.Empty) + ").")
    {
        HttpStatus = httpStatus;
        ProviderErrorCode = providerErrorCode;
    }

    public SmsGatewayException(string message, Exception inner) : base(message, inner)
    {
    }

    public int HttpStatus { get; }
    public int? ProviderErrorCode { get; }
}
