using System;
using System.Net;

namespace Microsoft.eShopWeb.Infrastructure.Twilio;

/// <summary>
/// Raised when a Twilio API call returns an error response. Carries the provider's error code and
/// message (per the spec's error model) but never any credential material.
/// </summary>
public class TwilioApiException : Exception
{
    public TwilioApiException(HttpStatusCode statusCode, int? providerErrorCode, string message)
        : base(message)
    {
        StatusCode = statusCode;
        ProviderErrorCode = providerErrorCode;
    }

    public HttpStatusCode StatusCode { get; }
    public int? ProviderErrorCode { get; }
}
