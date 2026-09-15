using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised for payment/fulfilment problems that are the caller's or operator's to resolve
/// (e.g. an order in the wrong state, a refund that would exceed what was captured, an
/// authorization that can no longer be renewed). The message is safe to surface to the caller.
/// </summary>
public class PaymentException : Exception
{
    public PaymentException(string message) : base(message)
    {
    }

    public PaymentException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
