using System;

namespace Microsoft.eShopWeb.Infrastructure.Services.Twilio;

/// <summary>
/// Raised when a Twilio API call fails. The message is deliberately built only from the provider's
/// numeric error code, HTTP status and documentation link — never from the free-text provider message
/// (which can echo the destination phone number) and never from any credential.
/// </summary>
public class TwilioApiException : Exception
{
    public int HttpStatus { get; }
    public int? ProviderCode { get; }

    public TwilioApiException(string operation, int httpStatus, int? providerCode, string? moreInfo)
        : base(BuildMessage(operation, httpStatus, providerCode, moreInfo))
    {
        HttpStatus = httpStatus;
        ProviderCode = providerCode;
    }

    private static string BuildMessage(string operation, int httpStatus, int? providerCode, string? moreInfo)
    {
        var codePart = providerCode.HasValue ? $" provider code {providerCode.Value}" : string.Empty;
        var infoPart = string.IsNullOrWhiteSpace(moreInfo) ? string.Empty : $" ({moreInfo})";
        return $"Twilio {operation} failed: HTTP {httpStatus}{codePart}{infoPart}";
    }
}
