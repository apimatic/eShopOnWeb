using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Wraps a failure reported by the payment provider. <see cref="IsProviderRejection"/>
/// distinguishes a deliberate provider rejection (bad card, validation failure - the
/// caller can act on it) from a transport/unknown failure (the provider is unreachable or
/// answered with something we could not interpret).
/// </summary>
public class PaymentGatewayException : Exception
{
    public PaymentGatewayException(string message, bool isProviderRejection, Exception? innerException = null)
        : base(message, innerException)
    {
        IsProviderRejection = isProviderRejection;
    }

    public bool IsProviderRejection { get; }
}
