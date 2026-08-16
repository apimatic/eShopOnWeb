using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// A payment operation could not be completed for a reason the caller (shopper or operator) can act
/// on — e.g. the card was declined, or an authorization expired and cannot be renewed. Carries a
/// message written in terms the caller can understand and act on.
/// </summary>
public class PaymentException : Exception
{
    public PaymentException(string message) : base(message) { }

    public PaymentException(string message, Exception innerException) : base(message, innerException) { }
}
