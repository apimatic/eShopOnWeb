using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Thrown when a payment operation violates a domain rule the caller can act on — for example a refund
/// larger than the captured amount, or an operation attempted from a state that does not allow it.
/// </summary>
public class PaymentValidationException : Exception
{
    public PaymentValidationException(string message) : base(message)
    {
    }
}
