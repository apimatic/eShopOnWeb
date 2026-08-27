using System;
using System.Net;

namespace Microsoft.eShopWeb.Infrastructure.Services.Twilio;

/// <summary>
/// A non-success response from the Twilio API, carrying the provider's error model
/// (code/message/more_info) from the OpenAPI-described error shape.
/// </summary>
public class TwilioApiException : Exception
{
    public TwilioApiException(HttpStatusCode httpStatus, int? providerCode, string? providerMessage, string? moreInfo)
        : base($"Twilio API error {(int)httpStatus} (code {providerCode?.ToString() ?? "n/a"}): {providerMessage}")
    {
        HttpStatus = httpStatus;
        ProviderCode = providerCode;
        ProviderMessage = providerMessage;
        MoreInfo = moreInfo;
    }

    public HttpStatusCode HttpStatus { get; }
    public int? ProviderCode { get; }
    public string? ProviderMessage { get; }
    public string? MoreInfo { get; }
}
