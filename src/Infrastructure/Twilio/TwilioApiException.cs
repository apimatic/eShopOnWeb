using System;
using System.Net;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

/// <summary>
/// Raised when a Twilio API call returns an error. Carries the provider's error code so callers can
/// react/log without echoing any personally identifiable content from the provider's message text.
/// </summary>
public class TwilioApiException : Exception, ISafeLoggableException
{
    public HttpStatusCode HttpStatus { get; }
    public int? ProviderErrorCode { get; }

    public TwilioApiException(HttpStatusCode httpStatus, int? providerErrorCode, string message)
        : base(message)
    {
        HttpStatus = httpStatus;
        ProviderErrorCode = providerErrorCode;
    }

    /// <summary>A log-safe summary that never includes the provider's (potentially PII-bearing) message text.</summary>
    public string SafeSummary => $"Twilio API error (http {(int)HttpStatus}, code {ProviderErrorCode?.ToString() ?? "n/a"})";
}
