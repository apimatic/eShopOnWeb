using System;
using System.Net;

namespace Microsoft.eShopWeb.Infrastructure.Messaging;

/// <summary>
/// A messaging-API call failed. The message deliberately carries only the HTTP status and the
/// provider's numeric error code — never the raw provider response, which can echo a shopper's
/// phone number and must not leak into logs.
/// </summary>
public class TwilioApiException : Exception
{
    public HttpStatusCode StatusCode { get; }
    public string? ProviderErrorCode { get; }

    public TwilioApiException(HttpStatusCode statusCode, string? providerErrorCode, string operation)
        : base($"Twilio {operation} request failed (HTTP {(int)statusCode}{(providerErrorCode is null ? "" : $", code {providerErrorCode}")}).")
    {
        StatusCode = statusCode;
        ProviderErrorCode = providerErrorCode;
    }
}
