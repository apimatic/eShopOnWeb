using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// A failure at the payment provider boundary. Carries a caller-safe message; the provider's
/// HTTP status when known (4xx = the caller can act on it, null/5xx = provider-side or transport);
/// and PayPal's debug id for support correlation. Never carries card data or raw exception text.
/// </summary>
public class PaymentGatewayException : Exception
{
    public int? ProviderStatusCode { get; }
    public string? ProviderErrorName { get; }
    public string? DebugId { get; }

    public PaymentGatewayException(string message, int? providerStatusCode = null,
        string? providerErrorName = null, string? debugId = null, Exception? innerException = null)
        : base(message, innerException)
    {
        ProviderStatusCode = providerStatusCode;
        ProviderErrorName = providerErrorName;
        DebugId = debugId;
    }

    /// <summary>True when the provider rejected the call with a 4xx the caller can act on.</summary>
    public bool IsProviderRejection => ProviderStatusCode is >= 400 and < 500;
}
