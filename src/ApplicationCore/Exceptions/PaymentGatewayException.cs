using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when a call to the payment provider fails. Carries the provider's
/// error name and debug id (never any card data) for correlation.
/// </summary>
public class PaymentGatewayException : Exception
{
    public PaymentGatewayException(string message) : base(message)
    {
    }

    public PaymentGatewayException(string message, string? providerErrorName, string? providerDebugId,
        int? providerStatusCode = null)
        : base(message)
    {
        ProviderErrorName = providerErrorName;
        ProviderDebugId = providerDebugId;
        ProviderStatusCode = providerStatusCode;
    }

    public string? ProviderErrorName { get; }
    public string? ProviderDebugId { get; }
    public int? ProviderStatusCode { get; }
}
