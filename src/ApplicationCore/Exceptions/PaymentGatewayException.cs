using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Caller-safe failure at the payment-provider boundary. ProviderStatusCode carries the
/// provider's HTTP status when there was one; null means transport/unknown failure.
/// </summary>
public class PaymentGatewayException : Exception
{
    public PaymentGatewayException(string message, int? providerStatusCode = null, Exception? innerException = null)
        : base(message, innerException)
    {
        ProviderStatusCode = providerStatusCode;
    }

    public int? ProviderStatusCode { get; }
}
