using System;

namespace Microsoft.eShopWeb.Infrastructure.Services;

/// <summary>Raised when a Twilio messaging API call fails. Carries the provider error code and
/// HTTP status; never carries recipient PII.</summary>
public class TwilioApiException : Exception
{
    public int HttpStatus { get; }
    public int? ProviderErrorCode { get; }

    public TwilioApiException(int httpStatus, int? providerErrorCode, string message)
        : base(message)
    {
        HttpStatus = httpStatus;
        ProviderErrorCode = providerErrorCode;
    }
}
