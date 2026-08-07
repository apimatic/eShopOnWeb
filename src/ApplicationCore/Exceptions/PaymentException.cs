using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown for a bad payment request (e.g. neither/both a card and a saved card supplied, an unknown
/// catalog item, or an empty order). Maps to an HTTP 400.
/// </summary>
public class PaymentValidationException : Exception
{
    public PaymentValidationException(string message) : base(message) { }
}

/// <summary>
/// Thrown when a payment operation is not valid for the order's current state (e.g. refunding an
/// order that was never paid). Maps to an HTTP 409.
/// </summary>
public class InvalidPaymentOperationException : Exception
{
    public InvalidPaymentOperationException(string message) : base(message) { }
}
