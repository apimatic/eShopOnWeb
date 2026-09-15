using System;
using System.Net;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

public class TwilioApiException : Exception
{
    public TwilioApiException(int? providerCode, HttpStatusCode statusCode)
        : base($"Twilio request failed with HTTP {(int)statusCode}" + (providerCode is null ? "." : $" and provider code {providerCode}."))
    {
        ProviderCode = providerCode;
        StatusCode = statusCode;
    }

    public int? ProviderCode { get; }
    public HttpStatusCode StatusCode { get; }
}
