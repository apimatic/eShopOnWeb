using System;
using System.Net;

namespace Microsoft.eShopWeb.Infrastructure.Sms;

/// <summary>
/// Raised when a messaging-provider call fails. Carries the HTTP status and the provider's
/// error code/message (never the auth secret or a phone number) for diagnostics.
/// </summary>
public class TwilioApiException : Exception
{
    public HttpStatusCode StatusCode { get; }
    public int? ProviderErrorCode { get; }

    public TwilioApiException(HttpStatusCode statusCode, int? providerErrorCode, string message)
        : base(message)
    {
        StatusCode = statusCode;
        ProviderErrorCode = providerErrorCode;
    }
}
