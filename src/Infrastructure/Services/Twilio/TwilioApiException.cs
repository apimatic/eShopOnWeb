using System;
using System.Net;

namespace Microsoft.eShopWeb.Infrastructure.Services.Twilio;

/// <summary>
/// Raised when the provider returns an error response. Carries the provider's own error model
/// (<c>code</c>, <c>message</c>, <c>status</c>) as described by the api-specs contract. The
/// message deliberately does not include any destination phone number.
/// </summary>
public class TwilioApiException : Exception
{
    public TwilioApiException(HttpStatusCode httpStatus, int? code, string? providerMessage)
        : base(BuildMessage(httpStatus, code, providerMessage))
    {
        HttpStatus = httpStatus;
        Code = code;
        ProviderMessage = providerMessage;
    }

    public HttpStatusCode HttpStatus { get; }
    public int? Code { get; }
    public string? ProviderMessage { get; }

    private static string BuildMessage(HttpStatusCode httpStatus, int? code, string? providerMessage)
    {
        var codePart = code.HasValue ? $" (code {code})" : string.Empty;
        return $"Twilio API returned {(int)httpStatus} {httpStatus}{codePart}: {providerMessage}";
    }
}
