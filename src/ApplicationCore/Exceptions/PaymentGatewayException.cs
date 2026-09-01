using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public enum PaymentGatewayErrorKind
{
    Unknown = 0,
    Validation = 1,
    NotFound = 2,
    Conflict = 3,
    PayerActionRequired = 4,
    Unavailable = 5
}

/// <summary>
/// The single failure type that crosses the payment-gateway boundary.
/// Carries a caller-safe message and the provider's HTTP status when there is one.
/// </summary>
public class PaymentGatewayException : Exception
{
    public PaymentGatewayException(PaymentGatewayErrorKind kind, string message, int? providerStatusCode = null, Exception? innerException = null)
        : base(message, innerException)
    {
        Kind = kind;
        ProviderStatusCode = providerStatusCode;
    }

    public PaymentGatewayErrorKind Kind { get; }
    public int? ProviderStatusCode { get; }
}
