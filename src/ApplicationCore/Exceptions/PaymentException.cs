using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when a payment operation cannot be completed. The message is safe to surface to the caller.
/// </summary>
public class PaymentException : Exception
{
    public PaymentException(string message) : base(message) { }

    public PaymentException(string message, Exception innerException) : base(message, innerException) { }
}
