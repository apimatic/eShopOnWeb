using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when a payment action is not valid for the order's current state — e.g. paying an order
/// that is not awaiting payment, cancelling one already captured, refunding more than was captured,
/// or a stale authorization that can no longer be renewed. The message is written to be actionable
/// by an operator or the caller.
/// </summary>
public class PaymentConflictException : Exception
{
    public PaymentConflictException(string message) : base(message)
    {
    }
}
