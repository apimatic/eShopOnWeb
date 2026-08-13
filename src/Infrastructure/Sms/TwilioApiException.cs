using System;

namespace Microsoft.eShopWeb.Infrastructure.Sms;

/// <summary>
/// Raised when a Twilio messaging/lookup API call fails. The message is deliberately built from the
/// provider's error code and status only — never its free-text detail, which can echo the destination
/// number back and so must not reach logs.
/// </summary>
public class TwilioApiException : Exception
{
    public int HttpStatus { get; }
    public int? ProviderErrorCode { get; }

    public TwilioApiException(int httpStatus, int? providerErrorCode, string? moreInfo)
        : base(BuildMessage(httpStatus, providerErrorCode, moreInfo))
    {
        HttpStatus = httpStatus;
        ProviderErrorCode = providerErrorCode;
    }

    private static string BuildMessage(int httpStatus, int? providerErrorCode, string? moreInfo)
    {
        var code = providerErrorCode.HasValue ? $" (provider code {providerErrorCode})" : string.Empty;
        var info = string.IsNullOrEmpty(moreInfo) ? string.Empty : $" See {moreInfo}.";
        return $"Twilio messaging API returned HTTP {httpStatus}{code}.{info}";
    }
}
