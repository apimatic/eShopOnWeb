using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when a payment operation is attempted against an order whose current status does not
/// permit it (e.g. capturing an order that was never authorized, or refunding beyond the
/// captured amount). Maps to a 409/422 at the API boundary.
/// </summary>
public class InvalidOrderPaymentStateException : Exception
{
    public InvalidOrderPaymentStateException(string message) : base(message)
    {
    }
}
