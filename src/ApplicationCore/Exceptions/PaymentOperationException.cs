using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when a payment operation is not valid for the order's current state (e.g. refunding an
/// order that was never paid). Maps to an HTTP 409 Conflict at the API boundary.
/// </summary>
public class PaymentOperationException : Exception
{
    public PaymentOperationException(string message) : base(message)
    {
    }
}
