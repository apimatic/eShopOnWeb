using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// A payment operation failed. The message is safe to surface to the caller and, for
/// operator actions, phrased so an operator can act on it.
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
