using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// A payment operation conflicted with the current state of the order/payment,
/// or the request was invalid. The message is safe to surface to API callers.
/// </summary>
public class PaymentException : Exception
{
    public PaymentException(string message) : base(message)
    {
    }
}
