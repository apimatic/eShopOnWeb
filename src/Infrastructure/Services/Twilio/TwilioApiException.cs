using System;

namespace Microsoft.eShopWeb.Infrastructure.Services.Twilio;

/// <summary>
/// Raised when a provider call returns an error. Carries the provider's own error code so the
/// caller can record it. The message is kept generic here so a shopper's number — which the
/// provider sometimes echoes into its error text — is never carried into logs.
/// </summary>
public class TwilioApiException : Exception
{
    public TwilioApiException(int statusCode, int? providerErrorCode, string? providerErrorMessage)
        : base($"Twilio request failed with HTTP {statusCode} (code {providerErrorCode?.ToString() ?? "n/a"}).")
    {
        StatusCode = statusCode;
        ProviderErrorCode = providerErrorCode;
        ProviderErrorMessage = providerErrorMessage;
    }

    public int StatusCode { get; }
    public int? ProviderErrorCode { get; }

    /// <summary>The provider's raw error text. May contain a phone number, so never log this.</summary>
    public string? ProviderErrorMessage { get; }
}
