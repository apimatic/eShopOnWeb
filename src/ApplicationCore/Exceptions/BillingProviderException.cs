using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when the billing provider rejects or fails to complete an operation.
/// This is the single typed error the billing seam surfaces for provider-side failures,
/// so callers never have to know which provider (or transport) is behind
/// <see cref="Interfaces.IBillingClient"/>.
/// </summary>
public class BillingProviderException : Exception
{
    public BillingProviderException(string message) : base(message)
    {
    }

    public BillingProviderException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public BillingProviderException(string message, int? statusCode, string? providerMessage = null, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        ProviderMessage = providerMessage;
    }

    /// <summary>HTTP status code reported by the provider, when one was available.</summary>
    public int? StatusCode { get; }

    /// <summary>The provider's own error text, when one was available.</summary>
    public string? ProviderMessage { get; }
}
