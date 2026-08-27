using System;
using System.Net;

namespace Microsoft.eShopWeb.Infrastructure.Services.Twilio;

/// <summary>
/// A non-success response from the provider's API. The provider's error message can
/// embed the destination phone number, so instances of this exception must never be
/// passed to a logger.
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
