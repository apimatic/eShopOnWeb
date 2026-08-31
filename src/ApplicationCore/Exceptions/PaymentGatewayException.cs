using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// A call to the payment provider failed. Carries the provider's HTTP status
/// (when one was received) and PayPal's debug id for support correlation.
/// </summary>
public class PaymentGatewayException : Exception
{
    public int? ProviderStatusCode { get; }
    public string? DebugId { get; }

    public PaymentGatewayException(string message, int? providerStatusCode = null, string? debugId = null, Exception? innerException = null)
        : base(message, innerException)
    {
        ProviderStatusCode = providerStatusCode;
        DebugId = debugId;
    }
}
