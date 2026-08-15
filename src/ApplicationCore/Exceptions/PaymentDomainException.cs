using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when a payment/order lifecycle operation is not valid for the current state
/// (for example, fulfilling an order that was never authorized, or refunding beyond the
/// captured amount). Maps to an HTTP 409 Conflict at the API boundary.
/// </summary>
public class PaymentDomainException : Exception
{
    public PaymentDomainException(string message) : base(message)
    {
    }
}
