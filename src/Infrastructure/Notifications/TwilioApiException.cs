using System;
using System.Net;

namespace Microsoft.eShopWeb.Infrastructure.Notifications;

/// <summary>
/// Raised when a Twilio REST call returns a non-success status. Carries the provider's error
/// envelope (<c>code</c>, <c>message</c>, <c>status</c>). Deliberately never carries a phone number.
/// </summary>
public class TwilioApiException : Exception
{
    public TwilioApiException(HttpStatusCode httpStatus, int? errorCode, string providerMessage)
        : base($"Twilio API call failed (HTTP {(int)httpStatus}{(errorCode is not null ? $", code {errorCode}" : string.Empty)}): {providerMessage}")
    {
        HttpStatus = httpStatus;
        ErrorCode = errorCode;
        ProviderMessage = providerMessage;
    }

    public HttpStatusCode HttpStatus { get; }
    public int? ErrorCode { get; }
    public string ProviderMessage { get; }
}
