using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when a payment operation is requested in a state where it is not valid (e.g. refunding an order
/// that was never captured, or refunding beyond the captured amount). Maps to a 4xx at the API boundary.
/// </summary>
public class PaymentStateException : Exception
{
    public PaymentStateException(string message) : base(message)
    {
    }
}
