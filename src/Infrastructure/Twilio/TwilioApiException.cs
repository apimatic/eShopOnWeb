using System.Net;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

/// <summary>
/// Raised when a Twilio API call returns an error. Carries the HTTP status and the provider's error
/// model (<c>code</c>, <c>message</c>, <c>more_info</c>) as described by the spec. The provider's
/// message text may echo the destination number, so callers must not log <see cref="ProviderMessage"/>
/// directly — log <see cref="SmsProviderException.ProviderErrorCode"/> and the HTTP status instead.
/// </summary>
public class TwilioApiException : SmsProviderException
{
    public TwilioApiException(HttpStatusCode httpStatus, int? providerErrorCode, string? providerMessage, string? moreInfo)
        : base((int)httpStatus, providerErrorCode)
    {
        HttpStatus = httpStatus;
        ProviderMessage = providerMessage;
        MoreInfo = moreInfo;
    }

    public HttpStatusCode HttpStatus { get; }

    /// <summary>The provider's error message. May contain the destination number — never log it.</summary>
    public string? ProviderMessage { get; }

    public string? MoreInfo { get; }
}
