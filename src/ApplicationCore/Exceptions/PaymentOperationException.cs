using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public enum PaymentErrorKind
{
    /// <summary>The resource does not exist or does not belong to the caller (mapped to 404).</summary>
    NotFound,
    /// <summary>The request is not valid for the resource's current state (mapped to 409).</summary>
    Conflict,
    /// <summary>The request is malformed or violates a rule such as over-refunding (mapped to 422/400).</summary>
    Validation
}

/// <summary>
/// A caller-actionable failure in a payment operation (bad state, not found, invalid amount). Distinct
/// from <see cref="Interfaces.Payments.PaymentGatewayException"/>, which represents a PayPal-side failure.
/// </summary>
public class PaymentOperationException : Exception
{
    public PaymentOperationException(string message, PaymentErrorKind kind) : base(message)
    {
        Kind = kind;
    }

    public PaymentErrorKind Kind { get; }
}
