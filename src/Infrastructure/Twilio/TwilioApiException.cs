using System;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

/// <summary>
/// Raised when a Twilio API call returns an error. Carries the provider's own error model (code / status)
/// as described by the OpenAPI spec.
/// </summary>
public class TwilioApiException : Exception
{
    public TwilioApiException(int httpStatus, int? providerErrorCode, string message, string? moreInfo = null)
        : base(message)
    {
        HttpStatus = httpStatus;
        ProviderErrorCode = providerErrorCode;
        MoreInfo = moreInfo;
    }

    /// <summary>The HTTP status code of the provider response.</summary>
    public int HttpStatus { get; }

    /// <summary>The provider's own error code, when present.</summary>
    public int? ProviderErrorCode { get; }

    public string? MoreInfo { get; }
}
