using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when the SMS provider returns an error. Carries a safe, non-PII summary; provider error
/// text (which may echo the destination number) is intentionally not exposed here so it is never
/// logged. Infrastructure provider clients throw a subclass of this so ApplicationCore can classify
/// failures without depending on the concrete provider.
/// </summary>
public class SmsProviderException : Exception
{
    public SmsProviderException(int httpStatusCode, int? providerErrorCode)
        : base($"SMS provider call failed with HTTP {httpStatusCode} (error code {providerErrorCode?.ToString() ?? "n/a"}).")
    {
        HttpStatusCode = httpStatusCode;
        ProviderErrorCode = providerErrorCode;
    }

    public int HttpStatusCode { get; }
    public int? ProviderErrorCode { get; }
}
