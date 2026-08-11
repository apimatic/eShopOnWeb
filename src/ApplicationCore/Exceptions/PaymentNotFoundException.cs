using System;

namespace Microsoft.eShopWeb.ApplicationCore.Exceptions;

/// <summary>
/// The requested order, payment or saved card does not exist, or does not belong to the
/// caller. The same exception is used for "missing" and "not yours" so that one shopper cannot
/// probe for the existence of another's resources.
/// </summary>
public class PaymentNotFoundException : Exception
{
    public PaymentNotFoundException(string message) : base(message) { }
}
