using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// A payment action was requested that the order's current state (or PayPal's own rules, e.g. an
/// authorization past its renewable window) does not allow. The message is written to be
/// operator-actionable.
/// </summary>
public class PaymentOperationNotAllowedException : Exception
{
    public PaymentOperationNotAllowedException(string message) : base(message)
    {
    }
}
