using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// Raised when a payment operation is rejected by PayPal (declined card, invalid data, etc.).
/// The message is safe to surface to the caller and never contains card data.
/// </summary>
public class PaymentFailedException : Exception
{
    public PaymentFailedException(string message) : base(message)
    {
    }

    public PaymentFailedException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
