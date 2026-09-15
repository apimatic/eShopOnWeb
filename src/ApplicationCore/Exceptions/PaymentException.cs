using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// A payment could not proceed for a reason the caller/operator can act on (e.g. the order is in the
/// wrong state, a refund would exceed the captured amount, or an authorization can no longer be renewed).
/// Surfaced to the API as 422 Unprocessable Entity with the message intact.
/// </summary>
public class PaymentException : Exception
{
    public PaymentException(string message) : base(message)
    {
    }

    public PaymentException(string message, Exception inner) : base(message, inner)
    {
    }
}
