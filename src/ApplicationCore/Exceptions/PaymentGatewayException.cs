using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// A call to the payment provider failed. Carries the provider's HTTP status when one was
/// received so the API boundary can distinguish a caller-actionable 4xx from a provider/transport
/// failure (5xx). The message is always caller-safe.
/// </summary>
public class PaymentGatewayException : Exception
{
    public PaymentGatewayException(string message, int? providerStatusCode = null, string? providerError = null, Exception? innerException = null)
        : base(message, innerException)
    {
        ProviderStatusCode = providerStatusCode;
        ProviderError = providerError;
    }

    public int? ProviderStatusCode { get; }
    public string? ProviderError { get; }
}
