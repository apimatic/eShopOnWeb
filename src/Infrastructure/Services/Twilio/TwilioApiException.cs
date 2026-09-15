using System;

namespace Microsoft.eShopWeb.Infrastructure.Services.Twilio;

/// <summary>
/// Raised when a Twilio messaging-API call returns an error. The message deliberately omits the
/// destination number so a shopper's number is never carried into logs.
/// </summary>
public class TwilioApiException : Exception
{
    public int HttpStatusCode { get; }
    public int? ProviderErrorCode { get; }

    public TwilioApiException(int httpStatusCode, int? providerErrorCode, string message)
        : base(message)
    {
        HttpStatusCode = httpStatusCode;
        ProviderErrorCode = providerErrorCode;
    }
}
