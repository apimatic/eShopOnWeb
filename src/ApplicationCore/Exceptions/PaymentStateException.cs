using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when a payment operation is not valid for the order's current lifecycle state
/// (e.g. fulfilling an unpaid order, cancelling after fulfilment, refunding beyond the capture).
/// </summary>
public class PaymentStateException : Exception
{
    public PaymentStateException(string message) : base(message) { }
}
