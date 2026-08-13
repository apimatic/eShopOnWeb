using System;
using System.Net;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised by the SMS provider gateway when a provider call fails (a non-2xx response, an unreadable
/// response, or the provider being unreachable). The message deliberately carries only non-sensitive
/// detail — an HTTP status, the provider's numeric error code and a documentation link — and never a
/// phone number or the auth token.
/// </summary>
public class SmsGatewayException : Exception
{
    public HttpStatusCode? StatusCode { get; }
    public int? ProviderErrorCode { get; }

    public SmsGatewayException(HttpStatusCode statusCode, int? providerErrorCode, string? moreInfo)
        : base(BuildMessage(statusCode, providerErrorCode, moreInfo))
    {
        StatusCode = statusCode;
        ProviderErrorCode = providerErrorCode;
    }

    public SmsGatewayException(string message) : base(message)
    {
    }

    public SmsGatewayException(string message, Exception innerException) : base(message, innerException)
    {
    }

    private static string BuildMessage(HttpStatusCode statusCode, int? providerErrorCode, string? moreInfo)
    {
        var message = $"Twilio messaging provider error (HTTP {(int)statusCode} {statusCode})";
        if (providerErrorCode.HasValue)
            message += $", provider code {providerErrorCode.Value}";
        if (!string.IsNullOrWhiteSpace(moreInfo))
            message += $". More info: {moreInfo}";
        return message;
    }
}
