using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when the billing provider rejects or cannot serve a request. This is the only provider
/// failure type that escapes the billing client, so no caller ever sees a provider-specific
/// exception type.
/// </summary>
public class BillingProviderException : Exception
{
    public BillingProviderException(int statusCode, string providerMessage)
        : base(BuildMessage(statusCode, providerMessage))
    {
        StatusCode = statusCode;
        ProviderMessage = providerMessage;
    }

    public BillingProviderException(int statusCode, string providerMessage, Exception innerException)
        : base(BuildMessage(statusCode, providerMessage), innerException)
    {
        StatusCode = statusCode;
        ProviderMessage = providerMessage;
    }

    /// <summary>HTTP status reported by the provider, or 0 when the provider could not be reached.</summary>
    public int StatusCode { get; }

    /// <summary>The provider's own description of the failure, safe to surface to an operator.</summary>
    public string ProviderMessage { get; }

    private static string BuildMessage(int statusCode, string providerMessage) => statusCode == 0
        ? $"The billing provider could not be reached: {providerMessage}"
        : $"The billing provider returned {statusCode}: {providerMessage}";
}
