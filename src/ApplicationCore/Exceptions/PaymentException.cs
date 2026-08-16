using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// A payment operation failed for a reason the caller/operator can act on. The message is safe to
/// surface to an operator (it never contains card details).
/// </summary>
public class PaymentException : Exception
{
    public PaymentException(string message) : base(message) { }
    public PaymentException(string message, Exception innerException) : base(message, innerException) { }
}
