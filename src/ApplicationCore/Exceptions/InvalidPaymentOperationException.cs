using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when a payment operation is not valid for the order's current state — e.g. fulfilling an
/// order that was never paid, cancelling one already fulfilled, or refunding beyond what was captured.
/// </summary>
public class InvalidPaymentOperationException : Exception
{
    public InvalidPaymentOperationException(string message) : base(message)
    {
    }
}
