using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

public enum PaymentOperationError
{
    /// <summary>The order or payment does not exist.</summary>
    NotFound,

    /// <summary>The caller is acting on data that is not theirs.</summary>
    Forbidden,

    /// <summary>The operation conflicts with the current payment state (e.g. already fulfilled).</summary>
    Conflict,

    /// <summary>The request is invalid for the current state (e.g. fulfilling an unpaid order).</summary>
    InvalidState
}

/// <summary>
/// An application-level failure of a payment operation that is the caller's to understand — a missing
/// order, an attempt to act on someone else's data, or an operation invalid for the current state.
/// Distinct from <see cref="PaymentGatewayException"/> (a PayPal/transport failure).
/// </summary>
public class PaymentOperationException : Exception
{
    public PaymentOperationError Error { get; }

    public PaymentOperationException(string message, PaymentOperationError error) : base(message)
    {
        Error = error;
    }
}
