using System;
using System.Net;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

/// <summary>
/// Raised when a Twilio API call returns an error. Carries Twilio's own error <see cref="Code"/> and message
/// (from the provider error model) but never any credential or recipient PII.
/// </summary>
public class TwilioApiException : Exception
{
    public TwilioApiException(HttpStatusCode httpStatus, int? code, string? providerMessage)
        : base($"Twilio API call failed (HTTP {(int)httpStatus}{(code is null ? "" : $", code {code}")}): {providerMessage}")
    {
        HttpStatus = httpStatus;
        Code = code;
        ProviderMessage = providerMessage;
    }

    public HttpStatusCode HttpStatus { get; }
    public int? Code { get; }
    public string? ProviderMessage { get; }
}
