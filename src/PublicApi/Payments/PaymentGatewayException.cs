using System;

namespace Microsoft.eShopWeb.PublicApi.Payments;

/// <summary>
/// Caller-safe failure at the payment provider boundary. Carries the provider's HTTP status
/// when known so the API layer can map 4xx to 4xx and everything else to 502.
/// </summary>
public class PaymentGatewayException : Exception
{
    public PaymentGatewayException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }

    public PaymentGatewayException(int? providerStatus, string message, Exception? inner = null)
        : base(message, inner)
    {
        ProviderStatus = providerStatus;
    }

    public int? ProviderStatus { get; }
}
