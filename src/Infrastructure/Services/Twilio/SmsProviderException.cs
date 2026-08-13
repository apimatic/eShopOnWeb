using System;

namespace Microsoft.eShopWeb.Infrastructure.Services.Twilio;

/// <summary>
/// Raised when the provider rejects or fails a messaging request. Its message deliberately carries only
/// the HTTP status and the provider's error code — never the destination number or the auth token — so it
/// is safe to log and safe to surface.
/// </summary>
public class SmsProviderException : Exception
{
    public int? StatusCode { get; }
    public int? ProviderErrorCode { get; }

    public SmsProviderException(string message, int? statusCode = null, int? providerErrorCode = null)
        : base(message)
    {
        StatusCode = statusCode;
        ProviderErrorCode = providerErrorCode;
    }
}
