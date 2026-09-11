using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when a payment operation cannot proceed for a business reason the caller/operator can
/// act on (e.g. an order in the wrong state, an over-refund, or an authorization that can no
/// longer be renewed). The message is safe to surface.
/// </summary>
public class PaymentOperationException : Exception
{
    public PaymentOperationException(string message) : base(message)
    {
    }
}
