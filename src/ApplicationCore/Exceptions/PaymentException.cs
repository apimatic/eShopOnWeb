using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// A payment operation could not be completed for a reason the caller can act on (invalid state,
/// over-refund, order not payable, etc.). Distinct from an unexpected failure calling PayPal.
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
