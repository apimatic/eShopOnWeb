using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// A payment operation was rejected because of the current state of the order/payment.
/// The message is intended to be actionable by the caller (shopper or operator).
/// </summary>
public class PaymentConflictException : Exception
{
    public PaymentConflictException(string message) : base(message)
    {
    }
}
