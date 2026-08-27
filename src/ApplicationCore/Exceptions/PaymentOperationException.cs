using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// A payment operation was attempted in a state that does not allow it
/// (e.g. paying an already-paid order, refunding more than was captured).
/// </summary>
public class PaymentOperationException : Exception
{
    public PaymentOperationException(string message) : base(message)
    {
    }
}
