using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// A payment-flow rule was violated (order/payment not found, wrong state for the requested transition,
/// or invalid input). Distinct from <see cref="PaymentGatewayException"/>, which is a provider failure.
/// Endpoints map <see cref="Kind"/> to an HTTP status.
/// </summary>
public class PaymentOperationException : Exception
{
    public PaymentOperationException(PaymentOperationErrorKind kind, string message) : base(message)
    {
        Kind = kind;
    }

    public PaymentOperationErrorKind Kind { get; }
}

public enum PaymentOperationErrorKind
{
    /// <summary>The order/payment/saved card does not exist or is not the caller's — 404.</summary>
    NotFound = 0,

    /// <summary>The payment is in the wrong state for this transition — 409.</summary>
    Conflict = 1,

    /// <summary>The request is malformed or the amount is out of range — 400.</summary>
    Validation = 2
}
