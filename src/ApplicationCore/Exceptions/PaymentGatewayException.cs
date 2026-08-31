using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// A failure at the payment provider boundary. Carries a caller-safe message and
/// whether the provider rejected the request (client-actionable) vs an outage.
/// </summary>
public class PaymentGatewayException : Exception
{
    public PaymentGatewayException(string message, bool isProviderRejection = false, Exception? innerException = null, string? providerErrorName = null)
        : base(message, innerException)
    {
        IsProviderRejection = isProviderRejection;
        ProviderErrorName = providerErrorName;
    }

    /// <summary>True when the provider rejected the call (4xx); false for transport/unknown failures.</summary>
    public bool IsProviderRejection { get; }

    /// <summary>The provider's error name (e.g. DUPLICATE_REQUEST_ID), when one was returned.</summary>
    public string? ProviderErrorName { get; }
}
