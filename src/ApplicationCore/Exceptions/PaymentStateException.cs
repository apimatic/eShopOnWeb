using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when a payment operation is not valid for the payment's current state (for example,
/// capturing a payment that is not authorized, or refunding beyond the captured amount). Surfaced
/// to the API as a 409 Conflict.
/// </summary>
public class PaymentStateException : Exception
{
    public PaymentStateException(string message) : base(message)
    {
    }

    public PaymentStateException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
