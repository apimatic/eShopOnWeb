using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// A call to the payment provider failed. Carries the provider's HTTP status when one exists
/// so the API boundary can surface client-actionable 4xx as-is and everything else as 502.
/// The message is always caller-safe.
/// </summary>
public class PaymentGatewayException : Exception
{
    public int? ProviderStatusCode { get; }

    public PaymentGatewayException(string message, int? providerStatusCode = null, Exception? innerException = null)
        : base(message, innerException)
    {
        ProviderStatusCode = providerStatusCode;
    }
}
